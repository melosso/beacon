using Beacon.Core.Models;
using Beacon.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Beacon.Storage;

public sealed class BrandIdentityService : IBrandIdentityService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private volatile IReadOnlyList<BrandIdentity> _cache = [];

    public BrandIdentityService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public void LoadInitialCache(List<BrandIdentity> identities) => _cache = identities;

    public IReadOnlyList<BrandIdentity> GetAll() => _cache;

    public BrandIdentity? GetById(int id) => _cache.FirstOrDefault(i => i.Id == id);

    public ValueTask<BrandIdentity> GetForBucketAsync(string bucket, CancellationToken ct = default)
    {
        var cached = _cache;
        foreach (var identity in cached)
        {
            if (identity.BucketMappings.Any(m => m.Bucket == bucket))
                return ValueTask.FromResult(identity);
        }
        var defaultIdentity = cached.FirstOrDefault(i => i.IsDefault) ?? cached.FirstOrDefault();
        return ValueTask.FromResult(defaultIdentity ?? new BrandIdentity { Id = 0, Name = "Default", IsDefault = true });
    }

    public async Task<BrandIdentity> CreateAsync(string name, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BeaconDbContext>();

            var identity = new BrandIdentity
            {
                Name = name,
                Settings = "{}",
                IsDefault = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.BrandIdentities.Add(identity);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            await RefreshCacheInternalAsync(db, ct).ConfigureAwait(false);
            return GetById(identity.Id)!;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<BrandIdentity> UpdateAsync(int id, string name, string settingsJson, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BeaconDbContext>();

            var entity = await db.BrandIdentities.FindAsync([id], ct).ConfigureAwait(false)
                ?? throw new KeyNotFoundException($"Brand identity {id} not found.");

            entity.Name = name;
            entity.Settings = settingsJson;
            entity.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            await RefreshCacheInternalAsync(db, ct).ConfigureAwait(false);
            return GetById(id)!;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BeaconDbContext>();

            var entity = await db.BrandIdentities.FindAsync([id], ct).ConfigureAwait(false)
                ?? throw new KeyNotFoundException($"Brand identity {id} not found.");

            if (entity.IsDefault)
                throw new InvalidOperationException("Cannot delete the default brand identity.");

            db.BrandIdentities.Remove(entity);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            await RefreshCacheInternalAsync(db, ct).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task AssignBucketsAsync(int id, IEnumerable<string> buckets, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BeaconDbContext>();

            var bucketList = buckets.ToList();

            var existing = await db.BucketIdentities
                .Where(b => bucketList.Contains(b.Bucket))
                .ToListAsync(ct).ConfigureAwait(false);
            db.BucketIdentities.RemoveRange(existing);

            var identityMappings = await db.BucketIdentities
                .Where(b => b.BrandIdentityId == id)
                .ToListAsync(ct).ConfigureAwait(false);
            db.BucketIdentities.RemoveRange(identityMappings);

            foreach (var bucket in bucketList)
            {
                db.BucketIdentities.Add(new BucketIdentity { Bucket = bucket, BrandIdentityId = id });
            }

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await RefreshCacheInternalAsync(db, ct).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task RefreshCacheInternalAsync(BeaconDbContext db, CancellationToken ct = default)
    {
        var identities = await db.BrandIdentities
            .Include(i => i.BucketMappings)
            .OrderBy(i => i.IsDefault ? 0 : 1)
            .ThenBy(i => i.Name)
            .ToListAsync(ct).ConfigureAwait(false);
        _cache = identities;
    }
}
