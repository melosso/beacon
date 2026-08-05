namespace Beacon.Core.Services;

public interface IBeaconCacheService
{
    Task RemoveAsync(string key, CancellationToken ct = default);
    Task<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan? ttl = null,
        CancellationToken ct = default) where T : class;
    Task FlushAsync(CancellationToken ct = default);
    int KeyCount { get; }
}
