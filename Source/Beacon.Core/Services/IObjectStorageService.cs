namespace Beacon.Core.Services;

public readonly record struct StorageObjectMeta(
    string Key,
    string ContentType,
    long SizeBytes,
    DateTimeOffset LastModified);

public interface IObjectStorageService
{
    Task<string> UploadAsync(string key, Stream data, string contentType, CancellationToken ct = default);
    Task<Stream> DownloadAsync(string key, CancellationToken ct = default);
    Task DeleteAsync(string key, CancellationToken ct = default);
    Task<bool> ExistsAsync(string key, CancellationToken ct = default);
    Task<StorageObjectMeta?> GetMetadataAsync(string key, CancellationToken ct = default);
    Task<bool> TestConnectionAsync(CancellationToken ct = default);
}
