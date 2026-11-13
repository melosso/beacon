using Beacon.Core.Models;

namespace Beacon.Core.Services;

public interface IConsentRepository
{
    Task<ConsentRecord?> GetAsync(string bucket, string emailHash, string permission);
    /// <summary>Inserts or updates the record subject to <see cref="ConsentTransition"/>. Returns false if no write occurs.</summary>
    /// <param name="insertOnly">If true: an existing record is never updated.</param>
    Task<bool> UpsertAsync(ConsentRecord record, string? actorId = null, bool insertOnly = false);
    Task<PagedResult<ConsentAuditEntry>> GetAuditAsync(string? bucket, string? emailHash, int page, int pageSize, CancellationToken ct = default, IReadOnlyList<string>? emailHashes = null);

    // Admin queries
    Task<IReadOnlyList<BucketInfo>> GetBucketsAsync();
    Task<BucketDetails> GetBucketDetailsAsync(string bucket);
    Task<PagedResult<EmailPermissions>> GetBucketRecordsAsync(string bucket, int page, int pageSize, string? sortBy = null, string? sortDir = null, string? search = null);
    Task<IReadOnlyList<EmailPermissions>> GetAllBucketRecordsAsync(string bucket);
    Task<int> DeleteBucketAsync(string bucket);
    Task<int> DeletePermissionAsync(string bucket, string permission);
    Task<int> DeleteRecordAsync(string bucket, string emailHash);
    Task<bool> EmailExistsInBucketAsync(string bucket, string emailHash);
    Task<IReadOnlyList<ConsentRecord>> GetByEmailAsync(string bucket, string emailHash);
    Task<PagedResult<IdentityInfo>> GetIdentitiesAsync(int page, int pageSize, string? sortBy = null, string? sortDir = null, string? search = null);
    Task<IReadOnlyList<(string EmailHash, string? EncryptedEmail, string? EncryptedName)>> GetEmailHashMappingsAsync();
    Task<PagedResult<IdentityInfo>> GetIdentitiesByHashesAsync(IReadOnlyList<string> hashes, int page, int pageSize, string? sortBy = null, string? sortDir = null);
    Task<Dictionary<string, EncryptedContact>> GetEncryptedEmailsForHashesAsync(IReadOnlyList<string> hashes, CancellationToken ct = default);
    Task<IdentityDetails?> GetIdentityDetailsAsync(string emailHash);
    Task<IDisposable> BeginTransactionAsync();
    Task CommitTransactionAsync();

    // Erasure
    Task DeleteAuditEntriesByEmailHashAsync(string emailHash, string bucket, CancellationToken ct = default);

    // Data policy operations
    Task<int> AnonymiseOptedOutAsync(DateTime cutoff, CancellationToken ct = default);
    /// <summary>Sets pending confirmation records older than <paramref name="cutoff"/> to OptedOut and removes their personal data.</summary>
    Task<int> PurgePendingConfirmationAsync(DateTime cutoff, CancellationToken ct = default);
    Task<int> CountOptedOutToAnonymiseAsync(DateTime cutoff, CancellationToken ct = default);
    Task<int> CountPendingConfirmationToPurgeAsync(DateTime cutoff, CancellationToken ct = default);
}

public sealed class BucketInfo
{
    public required string Name { get; init; }
    public int TotalEmails { get; init; }
    /// <summary>Consent records in the bucket: one per email and permission.</summary>
    public int TotalRecords { get; init; }
    public int OptedInRecords { get; init; }
    public IReadOnlyList<string> Permissions { get; init; } = [];
}

public sealed record EncryptedContact(string? EncryptedEmail, string? EncryptedName);

public sealed class IdentityInfo
{
    public required string EmailHash { get; init; }
    public string? Email { get; set; }
    public string? Name { get; set; }
    public int BucketCount { get; init; }
    public DateTime FirstSeen { get; init; }
    public DateTime LastChanged { get; init; }
}

public sealed class IdentityDetails
{
    public required string EmailHash { get; init; }
    public string? EncryptedEmail { get; init; }
    public string? EncryptedName { get; init; }
    public required IReadOnlyList<BucketSubscription> Subscriptions { get; init; }
}

public sealed class BucketDetails
{
    public required string Name { get; init; }
    public IReadOnlyList<string> Permissions { get; init; } = [];
    public IReadOnlyList<PermissionStats> Stats { get; init; } = [];
}

public sealed class PermissionStats
{
    public required string Permission { get; init; }
    public int OptedIn { get; init; }
    public int OptedOut { get; init; }
}

public sealed class EmailPermissions
{
    public required string EmailHash { get; init; }
    public string? EncryptedEmail { get; init; }
    public string? EncryptedName { get; init; }
    public string? Email { get; set; }
    public string? Name { get; set; }
    public required Dictionary<string, bool> Permissions { get; init; }
    public DateTime FirstSeen { get; init; }
    public DateTime LastChanged { get; init; }
    public Dictionary<string, string>? CustomFields { get; init; }
}

public sealed class BucketSubscription
{
    public required string Bucket { get; init; }
    public required Dictionary<string, bool> Permissions { get; init; }
    public DateTime LastChanged { get; init; }
}

public sealed class PagedResult<T>
{
    public required IReadOnlyList<T> Records { get; init; }
    public int Total { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
}
