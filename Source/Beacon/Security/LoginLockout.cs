using System.Collections.Concurrent;

namespace Beacon.Security;

/// <summary>
/// Per-identity login throttle. The IP rate limiter alone is not enough: it is per-address, so a
/// distributed attempt against one account slips under it.
/// </summary>
public sealed class LoginLockout
{
    private const int MaxFailures = 10;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<string, Attempts> _attempts = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeProvider _time;

    public LoginLockout(TimeProvider? timeProvider = null) => _time = timeProvider ?? TimeProvider.System;

    public bool IsLocked(string key, out TimeSpan retryAfter)
    {
        retryAfter = TimeSpan.Zero;
        if (!_attempts.TryGetValue(key, out var a) || a.LockedUntilUtc is not { } until)
            return false;

        var now = _time.GetUtcNow().UtcDateTime;
        if (now >= until)
        {
            _attempts.TryRemove(key, out _);
            return false;
        }

        retryAfter = until - now;
        return true;
    }

    public void RecordFailure(string key)
    {
        var now = _time.GetUtcNow().UtcDateTime;

        _attempts.AddOrUpdate(
            key,
            _ => new Attempts(1, now, null),
            (_, existing) =>
            {
                if (now - existing.FirstFailureUtc > Window)
                    return new Attempts(1, now, null);

                var count = existing.Count + 1;
                return new Attempts(count, existing.FirstFailureUtc,
                    count >= MaxFailures ? now + LockDuration : existing.LockedUntilUtc);
            });

        Sweep(now);
    }

    public void Reset(string key) => _attempts.TryRemove(key, out _);

    private void Sweep(DateTime now)
    {
        if (_attempts.Count < 1000) return;

        foreach (var (key, a) in _attempts)
        {
            var stale = a.LockedUntilUtc is { } until ? now >= until : now - a.FirstFailureUtc > Window;
            if (stale) _attempts.TryRemove(key, out _);
        }
    }

    // Immutable so AddOrUpdate's compare-and-swap is the only writer.
    private readonly record struct Attempts(int Count, DateTime FirstFailureUtc, DateTime? LockedUntilUtc);
}
