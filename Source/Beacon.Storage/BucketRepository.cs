using Beacon.Core.Models;
using Beacon.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Storage;

public class BucketRepository : IBucketRepository
{
    private readonly BeaconDbContext _db;
    private readonly IBeaconCacheService _cache;
    private readonly ISystemConfigurationService _config;

    public BucketRepository(BeaconDbContext db, IBeaconCacheService cache, ISystemConfigurationService config)
    {
        _db = db;
        _cache = cache;
        _config = config;
    }

    public async Task<bool> IsArchivedAsync(string bucket)
    {
        return await _db.ArchivedBuckets.AnyAsync(a => a.Bucket == bucket);
    }

    public async Task ArchiveAsync(string bucket)
    {
        var exists = await _db.ArchivedBuckets.AnyAsync(a => a.Bucket == bucket);
        if (!exists)
        {
            _db.ArchivedBuckets.Add(new ArchivedBucket
            {
                Bucket = bucket,
                ArchivedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
        }
    }

    public async Task UnarchiveAsync(string bucket)
    {
        var entry = await _db.ArchivedBuckets.FindAsync(bucket);
        if (entry != null)
        {
            _db.ArchivedBuckets.Remove(entry);
            await _db.SaveChangesAsync();
        }
    }

    public async Task<bool> AddPermissionAsync(string bucket, string permission)
    {
        var exists = await _db.BucketPermissions
            .AnyAsync(bp => bp.Bucket == bucket && bp.Permission == permission);
        if (exists) return false;

        _db.BucketPermissions.Add(new BucketPermission
        {
            Bucket = bucket,
            Permission = permission,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        await _cache.RemoveAsync(CacheKeys.BucketPermissions);
        return true;
    }

    public async Task<IReadOnlyList<string>> GetPermissionsAsync(string bucket)
    {
        return await _db.BucketPermissions
            .Where(bp => bp.Bucket == bucket)
            .Select(bp => bp.Permission)
            .OrderBy(p => p)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<string>> GetAllBucketNamesAsync()
    {
        return await _db.BucketPermissions
            .Select(bp => bp.Bucket)
            .Distinct()
            .OrderBy(b => b)
            .ToListAsync();
    }

    public async Task<Dictionary<string, List<string>>> GetAllPermissionsGroupedAsync()
    {
        var cfg = _config.Get();
        if (cfg.EnableCaching && cfg.CacheBucketData)
        {
            var ttl = TimeSpan.FromSeconds(cfg.CacheTtlSeconds);
            return await _cache.GetOrCreateAsync(
                CacheKeys.BucketPermissions,
                ct => FetchAllPermissionsGroupedAsync(ct),
                ttl);
        }

        return await FetchAllPermissionsGroupedAsync();
    }

    private async Task<Dictionary<string, List<string>>> FetchAllPermissionsGroupedAsync(CancellationToken ct = default)
    {
        var rows = await _db.BucketPermissions
            .Select(bp => new { bp.Bucket, bp.Permission })
            .OrderBy(bp => bp.Permission)
            .ToListAsync(ct);

        return rows
            .GroupBy(bp => bp.Bucket)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Permission).ToList());
    }

    public async Task<HashSet<string>> GetArchivedBucketsAsync()
    {
        return (await _db.ArchivedBuckets
            .Select(a => a.Bucket)
            .ToListAsync())
            .ToHashSet();
    }

    public async Task RemovePermissionAsync(string bucket, string permission)
    {
        var entry = await _db.BucketPermissions.FindAsync(bucket, permission);
        if (entry != null)
        {
            _db.BucketPermissions.Remove(entry);
            await _db.SaveChangesAsync();
            await _cache.RemoveAsync(CacheKeys.BucketPermissions);
        }
    }

    public async Task DeleteBucketAsync(string bucket)
    {
        var entries = await _db.BucketPermissions
            .Where(bp => bp.Bucket == bucket)
            .ToListAsync();
        if (entries.Count > 0)
        {
            _db.BucketPermissions.RemoveRange(entries);
        }

        var archived = await _db.ArchivedBuckets.FindAsync(bucket);
        if (archived != null)
        {
            _db.ArchivedBuckets.Remove(archived);
        }

        if (entries.Count > 0 || archived != null)
        {
            await _db.SaveChangesAsync();
            await _cache.RemoveAsync(CacheKeys.BucketPermissions);
        }
    }

    public async Task<IDisposable> BeginTransactionAsync()
    {
        return await _db.Database.BeginTransactionAsync();
    }

    public async Task CommitTransactionAsync()
    {
        if (_db.Database.CurrentTransaction != null)
        {
            await _db.Database.CurrentTransaction.CommitAsync();
        }
    }
}
