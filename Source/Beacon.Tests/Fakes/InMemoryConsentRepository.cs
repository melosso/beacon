using Beacon.Core.Models;
using Beacon.Core.Services;

namespace Beacon.Tests.Fakes;

internal sealed class InMemoryConsentRepository : IConsentRepository
{
    private readonly Dictionary<string, ConsentRecord> _records = new();

    public Task<ConsentRecord?> GetAsync(string bucket, string emailHash, string permission)
    {
        var key = $"{bucket}:{emailHash}:{permission}";
        _records.TryGetValue(key, out var record);
        return Task.FromResult(record);
    }

    public Task UpsertAsync(ConsentRecord record, string? actorId = null)
    {
        var key = $"{record.Bucket}:{record.EmailHash}:{record.Permission}";
        _records[key] = record;
        return Task.CompletedTask;
    }

    public Task<PagedResult<ConsentAuditEntry>> GetAuditAsync(
        string? bucket, string? emailHash, int page, int pageSize, CancellationToken ct = default)
        => Task.FromResult(new PagedResult<ConsentAuditEntry>
            { Records = [], Total = 0, Page = page, PageSize = pageSize });

    public Task<IReadOnlyList<BucketInfo>> GetBucketsAsync()
    {
        var buckets = _records.Values
            .GroupBy(r => r.Bucket)
            .Select(g => new BucketInfo
            {
                Name = g.Key,
                TotalEmails = g.Select(r => r.EmailHash).Distinct().Count(),
                Permissions = g.Select(r => r.Permission).Distinct().ToList()
            })
            .ToList();
        return Task.FromResult<IReadOnlyList<BucketInfo>>(buckets);
    }

    public Task<BucketDetails> GetBucketDetailsAsync(string bucket)
    {
        var records = _records.Values.Where(r => r.Bucket == bucket).ToList();
        var permissions = records.Select(r => r.Permission).Distinct().ToList();

        return Task.FromResult(new BucketDetails
        {
            Name = bucket,
            Permissions = permissions,
            Stats = permissions.Select(p => new PermissionStats
            {
                Permission = p,
                OptedIn = records.Count(r => r.Permission == p && r.Status == ConsentStatus.OptedIn),
                OptedOut = records.Count(r => r.Permission == p && r.Status == ConsentStatus.OptedOut)
            }).ToList()
        });
    }

    public Task<PagedResult<EmailPermissions>> GetBucketRecordsAsync(string bucket, int page, int pageSize, string? sortBy = null, string? sortDir = null, string? search = null)
    {
        var bucketRecords = _records.Values.Where(r => r.Bucket == bucket).ToList();

        var emailGroups = bucketRecords
            .GroupBy(r => r.EmailHash)
            .Select(g => new EmailPermissions
            {
                EmailHash = g.Key,
                Permissions = g.ToDictionary(r => r.Permission, r => r.Status == ConsentStatus.OptedIn),
                LastChanged = g.Max(r => r.ChangedAt)
            })
            .OrderByDescending(e => e.LastChanged)
            .ToList();

        var total = emailGroups.Count;
        var pagedRecords = emailGroups
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return Task.FromResult(new PagedResult<EmailPermissions>
        {
            Records = pagedRecords,
            Total = total,
            Page = page,
            PageSize = pageSize
        });
    }

    public Task<IReadOnlyList<EmailPermissions>> GetAllBucketRecordsAsync(string bucket)
    {
        var bucketRecords = _records.Values.Where(r => r.Bucket == bucket).ToList();

        var emailGroups = bucketRecords
            .GroupBy(r => r.EmailHash)
            .Select(g => new EmailPermissions
            {
                EmailHash = g.Key,
                Permissions = g.ToDictionary(r => r.Permission, r => r.Status == ConsentStatus.OptedIn),
                LastChanged = g.Max(r => r.ChangedAt)
            })
            .OrderByDescending(e => e.LastChanged)
            .ToList();

        return Task.FromResult<IReadOnlyList<EmailPermissions>>(emailGroups);
    }

    public Task<int> DeleteRecordAsync(string bucket, string emailHash)
    {
        var keysToRemove = _records
            .Where(kv => kv.Value.Bucket == bucket && kv.Value.EmailHash == emailHash)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var key in keysToRemove)
            _records.Remove(key);

        return Task.FromResult(keysToRemove.Count);
    }

    public Task<int> DeleteBucketAsync(string bucket)
    {
        var keysToRemove = _records
            .Where(kv => kv.Value.Bucket == bucket)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var key in keysToRemove)
            _records.Remove(key);

        return Task.FromResult(keysToRemove.Count);
    }

    public Task<int> DeletePermissionAsync(string bucket, string permission)
    {
        var keysToRemove = _records
            .Where(kv => kv.Value.Bucket == bucket && kv.Value.Permission == permission)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var key in keysToRemove)
            _records.Remove(key);

        return Task.FromResult(keysToRemove.Count);
    }

    public Task<bool> EmailExistsInBucketAsync(string bucket, string emailHash)
    {
        return Task.FromResult(_records.Values.Any(r => r.Bucket == bucket && r.EmailHash == emailHash));
    }

    public Task<IReadOnlyList<ConsentRecord>> GetByEmailAsync(string bucket, string emailHash)
    {
        var records = _records.Values
            .Where(r => r.Bucket == bucket && r.EmailHash == emailHash)
            .ToList();
        return Task.FromResult<IReadOnlyList<ConsentRecord>>(records);
    }

    public Task<PagedResult<IdentityInfo>> GetIdentitiesAsync(int page, int pageSize, string? sortBy = null, string? sortDir = null, string? search = null)
        => throw new NotImplementedException();

    public Task<IReadOnlyList<(string EmailHash, string? EncryptedEmail)>> GetEmailHashMappingsAsync()
        => throw new NotImplementedException();

    public Task<PagedResult<IdentityInfo>> GetIdentitiesByHashesAsync(IReadOnlyList<string> hashes, int page, int pageSize, string? sortBy = null, string? sortDir = null)
        => throw new NotImplementedException();

    public Task<IdentityDetails?> GetIdentityDetailsAsync(string emailHash)
    {
        var subs = _records.Values
            .Where(r => r.EmailHash == emailHash)
            .GroupBy(r => r.Bucket)
            .Select(g => new BucketSubscription
            {
                Bucket = g.Key,
                Permissions = g.ToDictionary(r => r.Permission, r => r.Status == ConsentStatus.OptedIn),
                LastChanged = g.Max(r => r.ChangedAt)
            })
            .OrderBy(b => b.Bucket)
            .ToList();

        if (subs.Count == 0) return Task.FromResult<IdentityDetails?>(null);

        var details = new IdentityDetails
        {
            EmailHash = emailHash,
            EncryptedEmail = _records.Values
                .FirstOrDefault(r => r.EmailHash == emailHash && r.EncryptedEmail != null)?.EncryptedEmail,
            Subscriptions = subs
        };

        return Task.FromResult<IdentityDetails?>(details);
    }

    public Task<IDisposable> BeginTransactionAsync() => Task.FromResult<IDisposable>(new NoOpDisposable());
    public Task CommitTransactionAsync() => Task.CompletedTask;

    public Task<int> AnonymiseOptedOutAsync(DateTime cutoff, CancellationToken ct = default) => Task.FromResult(0);
    public Task<int> AnonymiseIpAddressesAsync(DateTime cutoff, CancellationToken ct = default) => Task.FromResult(0);
    public Task<int> PurgePendingConfirmationAsync(DateTime cutoff, CancellationToken ct = default) => Task.FromResult(0);
    public Task<int> CountOptedOutToAnonymiseAsync(DateTime cutoff, CancellationToken ct = default) => Task.FromResult(0);
    public Task<int> CountIpAddressesToAnonymiseAsync(DateTime cutoff, CancellationToken ct = default) => Task.FromResult(0);
    public Task<int> CountPendingConfirmationToPurgeAsync(DateTime cutoff, CancellationToken ct = default) => Task.FromResult(0);

    private sealed class NoOpDisposable : IDisposable
    {
        public void Dispose() { }
    }
}
