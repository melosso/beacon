namespace Beacon.Storage;

/// <summary>
/// Lets API endpoints wake the data policy worker immediately for a manual run.
/// </summary>
public sealed class DataPolicyTrigger
{
    private readonly SemaphoreSlim _signal = new(0, 1);
    private volatile bool _isManual;

    public void Signal()
    {
        _isManual = true;
        try { _signal.Release(); }
        catch (SemaphoreFullException) { /* a signal is already pending */ }
    }

    /// <summary>Returns true if the last signal was a manual trigger, then resets the flag.</summary>
    internal bool ConsumeIsManual()
    {
        var value = _isManual;
        _isManual = false;
        return value;
    }

    internal Task WaitAsync(CancellationToken cancellationToken) =>
        _signal.WaitAsync(cancellationToken);
}
