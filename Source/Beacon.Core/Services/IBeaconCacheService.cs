namespace Beacon.Core.Services;

public interface IBeaconCacheService
{
    ValueTask<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class;
    Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default) where T : class;
    Task RemoveAsync(string key, CancellationToken ct = default);
    Task<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan? ttl = null,
        CancellationToken ct = default) where T : class;
    Task RemoveByPrefixAsync(string keyPrefix, CancellationToken ct = default);
    Task FlushAsync(CancellationToken ct = default);
    int KeyCount { get; }
}
