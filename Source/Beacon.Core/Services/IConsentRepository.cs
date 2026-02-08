using Beacon.Core.Models;

namespace Beacon.Core.Services;

public interface IConsentRepository
{
    Task<ConsentRecord?> GetAsync(string bucket, string emailHash, string permission);
    Task UpsertAsync(ConsentRecord record);

    // Admin queries
    Task<IReadOnlyList<BucketInfo>> GetBucketsAsync();
    Task<BucketDetails> GetBucketDetailsAsync(string bucket);
    Task<PagedResult<EmailPermissions>> GetBucketRecordsAsync(string bucket, int page, int pageSize, string? sortBy = null, string? sortDir = null, string? search = null);
    Task<IReadOnlyList<EmailPermissions>> GetAllBucketRecordsAsync(string bucket);
    Task<int> DeleteBucketAsync(string bucket);
    Task<int> DeleteRecordAsync(string bucket, string emailHash);
    Task<bool> EmailExistsInBucketAsync(string bucket, string emailHash);
    Task<IReadOnlyList<ConsentRecord>> GetByEmailAsync(string bucket, string emailHash);
}

public sealed class BucketInfo
{
    public required string Name { get; init; }
    public int TotalEmails { get; init; }
    public IReadOnlyList<string> Permissions { get; init; } = [];
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
    public string? Email { get; set; }  // Decrypted for admin display
    public required Dictionary<string, bool> Permissions { get; init; }
    public DateTime LastChanged { get; init; }
    public Dictionary<string, string>? CustomFields { get; init; }
}

public sealed class PagedResult<T>
{
    public required IReadOnlyList<T> Records { get; init; }
    public int Total { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
}
