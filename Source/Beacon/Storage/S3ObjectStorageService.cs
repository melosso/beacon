using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Beacon.Core.Services;

namespace Beacon.Storage;

internal sealed class S3ObjectStorageService : IObjectStorageService
{
    private readonly IAmazonS3 _client;
    private readonly string _bucket;
    private readonly string _publicUrl;

    public S3ObjectStorageService(ISystemConfigurationService configService)
    {
        var cfg = configService.Get();
        _bucket = cfg.ObjectStorageBucket;
        _publicUrl = (cfg.ObjectStoragePublicUrl ?? string.Empty).TrimEnd('/');

        _client = CreateClient(
            cfg.ObjectStorageProvider,
            cfg.ObjectStorageEndpoint,
            cfg.ObjectStorageRegion,
            cfg.ObjectStorageAccessKey,
            cfg.ObjectStorageSecretKey);
    }

    /// <summary>
    /// AWS SDK v4 requires a region or a ServiceURL and throws without one, where v3 silently probed
    /// the environment. Falls back to us-east-1, which S3-compatible endpoints accept.
    /// </summary>
    internal static AmazonS3Client CreateClient(
        string? provider, string? endpoint, string? region, string? accessKey, string? secretKey)
    {
        var config = new AmazonS3Config
        {
            ForcePathStyle = provider is "r2" or "minio"
        };

        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            config.ServiceURL = endpoint;
            config.AuthenticationRegion = string.IsNullOrWhiteSpace(region) ? "us-east-1" : region;
        }
        else
        {
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(
                string.IsNullOrWhiteSpace(region) ? "us-east-1" : region);
        }

        return new AmazonS3Client(new BasicAWSCredentials(accessKey, secretKey), config);
    }

    public async Task<string> UploadAsync(string key, Stream data, string contentType, CancellationToken ct = default)
    {
        var request = new PutObjectRequest
        {
            BucketName = _bucket,
            Key = key,
            InputStream = data,
            ContentType = contentType,
            AutoCloseStream = false
        };
        await _client.PutObjectAsync(request, ct);

        // With a CDN or custom domain configured, hand back a URL the browser can actually fetch.
        // Without one the bare key is returned, which only resolves if the bucket is served elsewhere.
        return _publicUrl.Length > 0 ? $"{_publicUrl}/{key}" : key;
    }

    public async Task<Stream> DownloadAsync(string key, CancellationToken ct = default)
    {
        var response = await _client.GetObjectAsync(_bucket, key, ct);
        return response.ResponseStream;
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        await _client.DeleteObjectAsync(_bucket, key, ct);
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        try
        {
            await _client.GetObjectMetadataAsync(_bucket, key, ct);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task<StorageObjectMeta?> GetMetadataAsync(string key, CancellationToken ct = default)
    {
        try
        {
            var meta = await _client.GetObjectMetadataAsync(_bucket, key, ct);
            // AWS SDK v4 made LastModified nullable.
            return new StorageObjectMeta(
                key,
                meta.Headers.ContentType,
                meta.ContentLength,
                meta.LastModified is { } lastModified
                    ? new DateTimeOffset(lastModified, TimeSpan.Zero)
                    : default);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<bool> TestConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            await _client.GetBucketLocationAsync(_bucket, ct);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
