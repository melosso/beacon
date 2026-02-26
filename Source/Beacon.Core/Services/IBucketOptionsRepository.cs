using Beacon.Core.Models;

namespace Beacon.Core.Services;

public interface IBucketOptionsRepository
{
    Task<BucketOptions> GetAsync(string bucket);
    Task SaveAsync(BucketOptions options);
}
