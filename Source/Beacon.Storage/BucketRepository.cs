using Beacon.Core.Models;
using Beacon.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Storage;

public class BucketRepository : IBucketRepository
{
    private readonly BeaconDbContext _db;

    public BucketRepository(BeaconDbContext db)
    {
        _db = db;
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

    public async Task RemovePermissionAsync(string bucket, string permission)
    {
        var entry = await _db.BucketPermissions.FindAsync(bucket, permission);
        if (entry != null)
        {
            _db.BucketPermissions.Remove(entry);
            await _db.SaveChangesAsync();
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
        }
    }
}
