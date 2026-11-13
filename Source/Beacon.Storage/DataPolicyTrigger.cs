namespace Beacon.Storage;

/// <summary>
/// Lets API endpoints wake the data policy worker immediately for a manual run.
/// </summary>
public sealed class DataPolicyTrigger
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void Signal()
    {
        try { _signal.Release(); }
        catch (SemaphoreFullException) { return; }
    }

    internal Task WaitAsync(CancellationToken cancellationToken) =>
        _signal.WaitAsync(cancellationToken);
}
