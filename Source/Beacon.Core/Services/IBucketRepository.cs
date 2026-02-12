namespace Beacon.Core.Services;

public interface IBucketRepository
{
    Task<bool> IsArchivedAsync(string bucket);
    Task ArchiveAsync(string bucket);
    Task UnarchiveAsync(string bucket);
}
