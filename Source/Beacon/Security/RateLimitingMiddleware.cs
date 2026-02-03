using System.Collections.Concurrent;

namespace Beacon.Security;

public class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly RateLimitOptions _options;
    private readonly ConcurrentDictionary<string, RateLimitEntry> _clients = new();

    public RateLimitingMiddleware(RequestDelegate next, RateLimitOptions options)
    {
        _next = next;
        _options = options;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var clientId = GetClientIdentifier(context);
        var now = DateTime.UtcNow;

        var entry = _clients.AddOrUpdate(
            clientId,
            _ => new RateLimitEntry { Count = 1, WindowStart = now },
            (_, existing) =>
            {
                if (now - existing.WindowStart > _options.Window)
                {
                    return new RateLimitEntry { Count = 1, WindowStart = now };
                }
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
        {
            return forwarded.Split(',')[0].Trim();
        }
        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private void CleanupOldEntries(DateTime now)
    {
        var cutoff = now - _options.Window - _options.Window;
        foreach (var key in _clients.Keys)
        {
            if (_clients.TryGetValue(key, out var entry) && entry.WindowStart < cutoff)
            {
                _clients.TryRemove(key, out _);
            }
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
