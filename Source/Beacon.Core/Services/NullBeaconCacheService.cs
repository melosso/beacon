namespace Beacon.Core.Services;

public sealed class NullBeaconCacheService : IBeaconCacheService
{
    public int KeyCount => 0;

    public ValueTask<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class
        => ValueTask.FromResult<T?>(null);

    public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default) where T : class
        => Task.CompletedTask;

    public Task RemoveAsync(string key, CancellationToken ct = default)
        => Task.CompletedTask;

    public async Task<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan? ttl = null,
        CancellationToken ct = default) where T : class
        => await factory(ct);

    public Task RemoveByPrefixAsync(string keyPrefix, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task FlushAsync(CancellationToken ct = default)
        => Task.CompletedTask;
}
