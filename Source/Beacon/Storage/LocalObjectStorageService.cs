using Beacon.Core.Services;

namespace Beacon.Storage;

internal sealed class LocalObjectStorageService : IObjectStorageService
{
    private readonly string _rootPath;

    public LocalObjectStorageService(IWebHostEnvironment env)
    {
        _rootPath = Path.Combine(env.ContentRootPath, "storage");
        Directory.CreateDirectory(_rootPath);
    }

    public async Task<string> UploadAsync(string key, Stream data, string contentType, CancellationToken ct = default)
    {
        var fullPath = GetFullPath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await using var fs = File.Create(fullPath);
        await data.CopyToAsync(fs, ct);
        return key;
    }

    public Task<Stream> DownloadAsync(string key, CancellationToken ct = default)
    {
        var fullPath = GetFullPath(key);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Object not found: {key}");
        Stream stream = File.OpenRead(fullPath);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        var fullPath = GetFullPath(key);
        if (File.Exists(fullPath))
            File.Delete(fullPath);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string key, CancellationToken ct = default)
        => Task.FromResult(File.Exists(GetFullPath(key)));

    public Task<StorageObjectMeta?> GetMetadataAsync(string key, CancellationToken ct = default)
    {
        var fullPath = GetFullPath(key);
        if (!File.Exists(fullPath))
            return Task.FromResult<StorageObjectMeta?>(null);

        var info = new FileInfo(fullPath);
        return Task.FromResult<StorageObjectMeta?>(new StorageObjectMeta(
            key,
            "application/octet-stream",
            info.Length,
            new DateTimeOffset(info.LastWriteTimeUtc)));
    }

    public Task<bool> TestConnectionAsync(CancellationToken ct = default)
        => Task.FromResult(true);

    private string GetFullPath(string key)
        => Path.Combine(_rootPath, key.Replace('/', Path.DirectorySeparatorChar));
}
