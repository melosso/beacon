using System.Collections.Concurrent;
using Beacon.Core.Services;
using Microsoft.Extensions.Caching.Memory;

namespace Beacon.Services;

internal sealed class MemoryBeaconCacheService : IBeaconCacheService, IDisposable
{
    private readonly IMemoryCache _cache;
    private readonly TimeSpan _defaultTtl;
    private readonly ConcurrentDictionary<string, byte> _keys = new();

    public MemoryBeaconCacheService(IMemoryCache cache, TimeSpan defaultTtl)
    {
        _cache = cache;
        _defaultTtl = defaultTtl;
    }

    public int KeyCount => _keys.Count;

    public ValueTask<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class
        => ValueTask.FromResult(_cache.Get<T>(key));

    public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default) where T : class
    {
        var expiry = ttl ?? _defaultTtl;
        _cache.Set(key, value, expiry);
        _keys.TryAdd(key, 0);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken ct = default)
    {
        _cache.Remove(key);
        _keys.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public async Task<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan? ttl = null,
        CancellationToken ct = default) where T : class
    {
        if (_cache.TryGetValue(key, out T? cached) && cached is not null)
            return cached;

        var value = await factory(ct);
        var expiry = ttl ?? _defaultTtl;
        _cache.Set(key, value, expiry);
        _keys.TryAdd(key, 0);
        return value;
    }

    public Task RemoveByPrefixAsync(string keyPrefix, CancellationToken ct = default)
    {
        foreach (var key in _keys.Keys.Where(k => k.StartsWith(keyPrefix, StringComparison.Ordinal)))
        {
            _cache.Remove(key);
            _keys.TryRemove(key, out _);
        }
        return Task.CompletedTask;
    }

    public Task FlushAsync(CancellationToken ct = default)
    {
        foreach (var key in _keys.Keys)
            _cache.Remove(key);
        _keys.Clear();
        return Task.CompletedTask;
    }

    public void Dispose() { }
}
