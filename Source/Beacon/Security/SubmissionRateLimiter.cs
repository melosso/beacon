using System.Collections.Concurrent;

namespace Beacon.Security;

public sealed class SubmissionRateLimiter
{
    private readonly ConcurrentDictionary<string, List<DateTime>> _requests = new();

    public bool IsRateLimited(Guid formId, string clientIp, int maxRequestsPerMinute)
    {
        var key = $"{formId}:{clientIp}";
        var now = DateTime.UtcNow;
        var window = TimeSpan.FromMinutes(1);

        var timestamps = _requests.AddOrUpdate(
            key,
            _ => [now],
            (_, existing) =>
            {
                // Remove expired entries
                existing.RemoveAll(t => now - t > window);
                existing.Add(now);
                return existing;
            });

        // Cleanup old keys periodically
        if (Random.Shared.Next(100) == 0)
        {
            CleanupOldEntries(now, window);
        }

        return timestamps.Count > maxRequestsPerMinute;
    }

    private void CleanupOldEntries(DateTime now, TimeSpan window)
    {
        var cutoff = now - window - window;
        foreach (var key in _requests.Keys)
        {
            if (_requests.TryGetValue(key, out var timestamps))
            {
                if (timestamps.Count == 0 || timestamps.All(t => t < cutoff))
                {
                    _requests.TryRemove(key, out _);
                }
            }
        }
    }

    public static string GetClientIp(HttpContext context)
    {
        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
