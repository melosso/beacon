using System.Collections.Concurrent;

namespace Beacon.Security;

public class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly RateLimitOptions _options;
    private readonly ConcurrentDictionary<string, RateLimitEntry> _clients = new();
    private readonly ConcurrentDictionary<string, RateLimitEntry> _strictClients = new();

    public RateLimitingMiddleware(RequestDelegate next, RateLimitOptions options)
    {
        _next = next;
        _options = options;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var clientIp = GetClientIdentifier(context);

        // Loopback traffic is trusted dev/seed traffic; skip rate limiting.
        if (IsLoopback(clientIp))
        {
            await _next(context);
            return;
        }

        var now = DateTime.UtcNow;

        // Check path-specific strict limits first
        var path = context.Request.Path.Value ?? "";
        foreach (var strictLimit in _options.StrictPaths)
        {
            if (path.StartsWith(strictLimit.Key, StringComparison.OrdinalIgnoreCase))
            {
                var strictKey = $"{strictLimit.Key}:{clientIp}";
                var strictEntry = _strictClients.AddOrUpdate(
                    strictKey,
                    _ => new RateLimitEntry { Count = 1, WindowStart = now },
                    (_, existing) =>
                    {
                        if (now - existing.WindowStart > _options.Window)
                            return new RateLimitEntry { Count = 1, WindowStart = now };
                        existing.Count++;
                        return existing;
                    });

                if (strictEntry.Count > strictLimit.Value)
                {
                    context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                    context.Response.Headers.RetryAfter =
                        ((int)(_options.Window - (now - strictEntry.WindowStart)).TotalSeconds).ToString();
                    await context.Response.WriteAsJsonAsync(new { error = "Rate limit exceeded" });
                    return;
                }
                break;
            }
        }

        // Global rate limit
        var entry = _clients.AddOrUpdate(
            clientIp,
            _ => new RateLimitEntry { Count = 1, WindowStart = now },
            (_, existing) =>
            {
                if (now - existing.WindowStart > _options.Window)
                    return new RateLimitEntry { Count = 1, WindowStart = now };
                existing.Count++;
                return existing;
            });

        if (entry.Count > _options.MaxRequests)
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers.RetryAfter =
                ((int)(_options.Window - (now - entry.WindowStart)).TotalSeconds).ToString();
            await context.Response.WriteAsJsonAsync(new { error = "Rate limit exceeded" });
            return;
        }

        await _next(context);

        // Cleanup old entries periodically
        if (Random.Shared.Next(100) == 0)
        {
            CleanupOldEntries(now);
        }
    }

    private static string GetClientIdentifier(HttpContext context)
    {
        var forwarded = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwarded))
            return forwarded.Split(',')[0].Trim();
        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private static bool IsLoopback(string ip) =>
        ip is "127.0.0.1" or "::1" or "localhost" or "unknown";

    private void CleanupOldEntries(DateTime now)
    {
        var cutoff = now - _options.Window - _options.Window;
        foreach (var key in _clients.Keys)
        {
            if (_clients.TryGetValue(key, out var entry) && entry.WindowStart < cutoff)
                _clients.TryRemove(key, out _);
        }
        foreach (var key in _strictClients.Keys)
        {
            if (_strictClients.TryGetValue(key, out var entry) && entry.WindowStart < cutoff)
                _strictClients.TryRemove(key, out _);
        }
    }

    private class RateLimitEntry
    {
        public int Count { get; set; }
        public DateTime WindowStart { get; set; }
    }
}

public class RateLimitOptions
{
    public int MaxRequests { get; set; } = 1500;
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);
    /// <summary>Path prefixes mapped to their maximum requests per window.</summary>
    public Dictionary<string, int> StrictPaths { get; set; } = new();
}

public static class RateLimitingExtensions
{
    public static IApplicationBuilder UseRateLimiting(
        this IApplicationBuilder app,
        Action<RateLimitOptions>? configure = null)
    {
        var options = new RateLimitOptions();
        configure?.Invoke(options);
        return app.UseMiddleware<RateLimitingMiddleware>(options);
    }
}
