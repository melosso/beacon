using Beacon.Core.Models;

namespace Beacon.Core.Services;

public interface IBrandIdentityService
{
    IReadOnlyList<BrandIdentity> GetAll();
    BrandIdentity? GetById(int id);
    ValueTask<BrandIdentity> GetForBucketAsync(string bucket, CancellationToken ct = default);
    Task<BrandIdentity> CreateAsync(string name, CancellationToken ct = default);
    Task<BrandIdentity> UpdateAsync(int id, string name, string settingsJson, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
    Task AssignBucketsAsync(int id, IEnumerable<string> buckets, CancellationToken ct = default);
}
