namespace Beacon.Storage;

/// <summary>
/// Lets API endpoints wake the email queue worker immediately after enqueueing,
/// with a short cooldown so bulk token generation doesn't trigger a burst of batches.
/// </summary>
public sealed class EmailDispatchTrigger
{
    private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(10);

    private readonly SemaphoreSlim _signal = new(0, 1);
    private readonly object _lock = new();
    private DateTime _lastSignalAt = DateTime.MinValue;

    /// <summary>
    /// Signals the worker to process the queue now. No-op if called within the cooldown window
    /// or if a signal is already pending from a previous call.
    /// </summary>
    public void Signal()
    {
        bool shouldRelease;
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            shouldRelease = now - _lastSignalAt >= Cooldown;
            if (shouldRelease) _lastSignalAt = now;
        }

        if (!shouldRelease) return;

        try { _signal.Release(); }
        catch (SemaphoreFullException) { /* a signal is already pending */ }
    }

    internal Task WaitAsync(CancellationToken cancellationToken) =>
        _signal.WaitAsync(cancellationToken);
}
