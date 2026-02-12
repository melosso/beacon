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
}
