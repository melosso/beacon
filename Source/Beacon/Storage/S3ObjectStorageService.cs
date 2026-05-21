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

    public S3ObjectStorageService(ISystemConfigurationService configService)
    {
        var cfg = configService.Get();
        _bucket = cfg.ObjectStorageBucket;

        var credentials = new BasicAWSCredentials(
            cfg.ObjectStorageAccessKey,
            cfg.ObjectStorageSecretKey);

        var s3Config = new AmazonS3Config
        {
            ForcePathStyle = cfg.ObjectStorageProvider is "r2" or "minio"
        };

        if (!string.IsNullOrWhiteSpace(cfg.ObjectStorageEndpoint))
            s3Config.ServiceURL = cfg.ObjectStorageEndpoint;
        else if (!string.IsNullOrWhiteSpace(cfg.ObjectStorageRegion))
            s3Config.RegionEndpoint = RegionEndpoint.GetBySystemName(cfg.ObjectStorageRegion);

        _client = new AmazonS3Client(credentials, s3Config);
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
        return key;
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
            return new StorageObjectMeta(
                key,
                meta.Headers.ContentType,
                meta.ContentLength,
                meta.LastModified);
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
