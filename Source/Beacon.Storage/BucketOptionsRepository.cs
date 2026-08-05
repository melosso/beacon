using Beacon.Core.Models;
using Beacon.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Storage;

public sealed class BucketOptionsRepository {
    private readonly BeaconDbContext _db;

    public BucketOptionsRepository(BeaconDbContext db) => _db = db;

    public async Task<BucketOptions> GetAsync(string bucket)
        => await _db.BucketOptions.FirstOrDefaultAsync(b => b.Bucket == bucket)
           ?? new BucketOptions { Bucket = bucket, DoubleOptIn = true };

    public async Task SaveAsync(BucketOptions options)
    {
        var existing = await _db.BucketOptions.FirstOrDefaultAsync(b => b.Bucket == options.Bucket);
        if (existing is null)
        {
            options.UpdatedAt = DateTime.UtcNow;
            _db.BucketOptions.Add(options);
        }
        else
        {
            existing.DoubleOptIn = options.DoubleOptIn;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
    }
}
