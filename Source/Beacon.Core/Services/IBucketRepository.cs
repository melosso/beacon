namespace Beacon.Core.Services;

public interface IBucketRepository
{
    Task<bool> IsArchivedAsync(string bucket);
    Task ArchiveAsync(string bucket);
    Task UnarchiveAsync(string bucket);
    Task<bool> AddPermissionAsync(string bucket, string permission);
    Task<IReadOnlyList<string>> GetPermissionsAsync(string bucket);
    Task<IReadOnlyList<string>> GetAllBucketNamesAsync();
    Task RemovePermissionAsync(string bucket, string permission);
    Task DeleteBucketAsync(string bucket);
    Task<IDisposable> BeginTransactionAsync();
    Task CommitTransactionAsync();
}
