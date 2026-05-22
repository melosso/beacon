using System.Security.Cryptography;
using System.Text;
using Beacon.Core.Models;
using Beacon.Core.Security;

namespace Beacon.Core.Services;

public sealed class ConsentService : IConsentService
{
    private readonly IConsentRepository _repository;
    private readonly IBeaconCacheService _cache;
    private readonly ISystemConfigurationService _config;
    private readonly EmailHasher _emailHasher;
    private readonly Encryptor _encryptor;

    public ConsentService(
        IConsentRepository repository,
        IBeaconCacheService cache,
        ISystemConfigurationService config,
        EmailHasher emailHasher,
        Encryptor encryptor)
    {
        _repository = repository;
        _cache = cache;
        _config = config;
        _emailHasher = emailHasher;
        _encryptor = encryptor;
    }

    public async Task<ConsentStatus> CheckAsync(string bucket, string email, string permission)
    {
        var normalizedBucket = NormalizeBucket(bucket);
        var emailHash = _emailHasher.Hash(email);
        var normalizedPermission = NormalizePermission(permission);

        var cfg = _config.Get();
        if (cfg.EnableCaching && cfg.CacheConsentRecords)
        {
            var key = CacheKeys.Consent(normalizedBucket, emailHash, normalizedPermission);
            var ttl = TimeSpan.FromSeconds(cfg.CacheTtlSeconds);
            var box = await _cache.GetOrCreateAsync(
                key,
                ct => FetchConsentStatusBoxAsync(normalizedBucket, emailHash, normalizedPermission, ct),
                ttl);
            return box.Status;
        }

        return await FetchConsentStatusAsync(normalizedBucket, emailHash, normalizedPermission);
    }

    private async Task<ConsentStatusBox> FetchConsentStatusBoxAsync(
        string bucket, string emailHash, string permission, CancellationToken ct = default)
    {
        var record = await _repository.GetAsync(bucket, emailHash, permission);
        return new ConsentStatusBox(record?.Status ?? ConsentStatus.OptedIn);
    }

    private async Task<ConsentStatus> FetchConsentStatusAsync(
        string bucket, string emailHash, string permission, CancellationToken ct = default)
    {
        var record = await _repository.GetAsync(bucket, emailHash, permission);
        return record?.Status ?? ConsentStatus.OptedIn;
    }

    private sealed record ConsentStatusBox(ConsentStatus Status);

    public async Task ProcessOptOutAsync(string bucket, string email, string[] permissions, string token, ConsentSource source, string? customFieldsJson = null)
    {
        var normalizedBucket = NormalizeBucket(bucket);
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var emailHash = _emailHasher.Hash(normalizedEmail);
        var encryptedEmail = _encryptor.Encrypt(normalizedEmail);
        var tokenHash = ComputeTokenHash(token);

        foreach (var permission in permissions)
        {
            var normalizedPermission = NormalizePermission(permission);
            var record = new ConsentRecord
            {
                Id = Guid.NewGuid(),
                Bucket = normalizedBucket,
                EmailHash = emailHash,
                EncryptedEmail = encryptedEmail,
                Permission = normalizedPermission,
                Status = ConsentStatus.OptedOut,
                Source = source,
                ChangedAt = DateTime.UtcNow,
                TokenHash = tokenHash,
                CustomFields = customFieldsJson
            };

            await _repository.UpsertAsync(record);
            await _cache.RemoveAsync(CacheKeys.Consent(normalizedBucket, emailHash, normalizedPermission));
        }
    }

    public async Task OverrideAsync(string bucket, string email, string permission, ConsentStatus status,
        string? customFieldsJson = null, string? actorId = null,
        ConsentSource source = ConsentSource.Admin, string? consentText = null)
    {
        var normalizedBucket = NormalizeBucket(bucket);
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var emailHash = _emailHasher.Hash(normalizedEmail);
        var encryptedEmail = _encryptor.Encrypt(normalizedEmail);
        var normalizedPermission = NormalizePermission(permission);

        var record = new ConsentRecord
        {
            Id = Guid.NewGuid(),
            Bucket = normalizedBucket,
            EmailHash = emailHash,
            EncryptedEmail = encryptedEmail,
            Permission = normalizedPermission,
            Status = status,
            Source = source,
            ChangedAt = DateTime.UtcNow,
            CustomFields = customFieldsJson,
            ConsentText = consentText
        };

        await _repository.UpsertAsync(record, actorId);
        await _cache.RemoveAsync(CacheKeys.Consent(normalizedBucket, emailHash, normalizedPermission));
    }

    public async Task<bool> EnsureAsync(string bucket, string email, string permission, ConsentStatus status,
        string? customFieldsJson = null, string? consentText = null,
        ConsentSource source = ConsentSource.Admin, string? actorId = null)
    {
        var normalizedBucket = NormalizeBucket(bucket);
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var emailHash = _emailHasher.Hash(normalizedEmail);
        var normalizedPermission = NormalizePermission(permission);

        var existing = await _repository.GetAsync(normalizedBucket, emailHash, normalizedPermission);
        if (existing is not null)
            return false;

        var encryptedEmail = _encryptor.Encrypt(normalizedEmail);
        var record = new ConsentRecord
        {
            Id = Guid.NewGuid(),
            Bucket = normalizedBucket,
            EmailHash = emailHash,
            EncryptedEmail = encryptedEmail,
            Permission = normalizedPermission,
            Status = status,
            Source = source,
            ChangedAt = DateTime.UtcNow,
            CustomFields = customFieldsJson,
            ConsentText = consentText
        };

        await _repository.UpsertAsync(record, actorId);
        await _cache.RemoveAsync(CacheKeys.Consent(normalizedBucket, emailHash, normalizedPermission));
        return true;
    }

    public Task<IDisposable> BeginTransactionAsync() => _repository.BeginTransactionAsync();
    public Task CommitTransactionAsync() => _repository.CommitTransactionAsync();

    private static string ComputeTokenHash(string token)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string NormalizeBucket(string bucket)
    {
        return bucket.Trim().ToLowerInvariant();
    }

    private static string NormalizePermission(string permission)
    {
        return permission.Trim().ToLowerInvariant();
    }
}
