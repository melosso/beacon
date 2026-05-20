using System.Security.Cryptography;
using System.Text;
using Beacon.Core.Models;
using Beacon.Core.Security;

namespace Beacon.Core.Services;

public sealed class ConsentService : IConsentService
{
    private readonly IConsentRepository _repository;
    private readonly EmailHasher _emailHasher;
    private readonly Encryptor _encryptor;

    public ConsentService(
        IConsentRepository repository,
        EmailHasher emailHasher,
        Encryptor encryptor)
    {
        _repository = repository;
        _emailHasher = emailHasher;
        _encryptor = encryptor;
    }

    public async Task<ConsentStatus> CheckAsync(string bucket, string email, string permission)
    {
        var normalizedBucket = NormalizeBucket(bucket);
        var emailHash = _emailHasher.Hash(email);
        var normalizedPermission = NormalizePermission(permission);
        var record = await _repository.GetAsync(normalizedBucket, emailHash, normalizedPermission);
        return record?.Status ?? ConsentStatus.OptedIn;
    }

    public async Task ProcessOptOutAsync(string bucket, string email, string[] permissions, string token, ConsentSource source, string? ipAddress = null)
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
                IpAddress = ipAddress
            };

            await _repository.UpsertAsync(record);
        }
    }

    public async Task OverrideAsync(string bucket, string email, string permission, ConsentStatus status,
        string? customFieldsJson = null, string? actorId = null,
        ConsentSource source = ConsentSource.Admin, string? ipAddress = null, string? consentText = null)
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
            IpAddress = ipAddress,
            ConsentText = consentText
        };

        await _repository.UpsertAsync(record, actorId);
    }

    public async Task<bool> EnsureAsync(string bucket, string email, string permission, ConsentStatus status,
        string? customFieldsJson = null, string? ipAddress = null, string? consentText = null,
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
            IpAddress = ipAddress,
            ConsentText = consentText
        };

        await _repository.UpsertAsync(record, actorId);
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
