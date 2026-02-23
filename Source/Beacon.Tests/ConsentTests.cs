using Beacon.Core.Models;
using Beacon.Core.Security;
using Beacon.Core.Services;
using Xunit;

namespace Beacon.Tests;

public class ConsentTests
{
    private const string TestPepper = "test-pepper-for-hashing";
    private const string TestEncryptionKey = "MDEyMzQ1Njc4OTAxMjM0NTY3ODkwMTIzNDU2Nzg5MDE=";  // 32 bytes
    private const string TestBucket = "test-bucket";

    private ConsentService CreateService(InMemoryConsentRepository repository)
    {
        var emailHasher = new EmailHasher(TestPepper);
        var encryptor = new Encryptor(TestEncryptionKey);
        return new ConsentService(repository, emailHasher, encryptor);
    }

    [Fact]
    public async Task ConsentService_CheckAsync_ReturnsOptedInByDefault()
    {
        var repository = new InMemoryConsentRepository();
        var service = CreateService(repository);

        var status = await service.CheckAsync(TestBucket, "new@example.com", "newsletter");

        Assert.Equal(ConsentStatus.OptedIn, status);
    }

    [Fact]
    public async Task ConsentService_ProcessOptOut_SetsOptedOut()
    {
        var repository = new InMemoryConsentRepository();
        var service = CreateService(repository);

        await service.ProcessOptOutAsync(TestBucket, "test@example.com", ["newsletter"], "token123", ConsentSource.Url);

        var status = await service.CheckAsync(TestBucket, "test@example.com", "newsletter");
        Assert.Equal(ConsentStatus.OptedOut, status);
    }

    [Fact]
    public async Task ConsentService_ProcessOptOut_HandlesMultiplePermissions()
    {
        var repository = new InMemoryConsentRepository();
        var service = CreateService(repository);

        await service.ProcessOptOutAsync(TestBucket, "test@example.com", ["newsletter", "alerts"], "token123", ConsentSource.Url);

        var newsletterStatus = await service.CheckAsync(TestBucket, "test@example.com", "newsletter");
        var alertsStatus = await service.CheckAsync(TestBucket, "test@example.com", "alerts");

        Assert.Equal(ConsentStatus.OptedOut, newsletterStatus);
        Assert.Equal(ConsentStatus.OptedOut, alertsStatus);
    }

    [Fact]
    public async Task ConsentService_Override_ChangesStatus()
    {
        var repository = new InMemoryConsentRepository();
        var service = CreateService(repository);

        await service.ProcessOptOutAsync(TestBucket, "test@example.com", ["newsletter"], "token123", ConsentSource.Url);
        var statusBefore = await service.CheckAsync(TestBucket, "test@example.com", "newsletter");

        await service.OverrideAsync(TestBucket, "test@example.com", "newsletter", ConsentStatus.OptedIn);
        var statusAfter = await service.CheckAsync(TestBucket, "test@example.com", "newsletter");

        Assert.Equal(ConsentStatus.OptedOut, statusBefore);
        Assert.Equal(ConsentStatus.OptedIn, statusAfter);
    }

    [Fact]
    public async Task ConsentService_NormalizesEmail()
    {
        var repository = new InMemoryConsentRepository();
        var service = CreateService(repository);

        await service.ProcessOptOutAsync(TestBucket, "TEST@EXAMPLE.COM", ["newsletter"], "token123", ConsentSource.Url);

        var status = await service.CheckAsync(TestBucket, "  test@example.com  ", "newsletter");
        Assert.Equal(ConsentStatus.OptedOut, status);
    }

    [Fact]
    public async Task ConsentService_DifferentBuckets_AreIsolated()
    {
        var repository = new InMemoryConsentRepository();
        var service = CreateService(repository);

        await service.ProcessOptOutAsync("bucket-a", "test@example.com", ["newsletter"], "token123", ConsentSource.Url);

        var statusBucketA = await service.CheckAsync("bucket-a", "test@example.com", "newsletter");
        var statusBucketB = await service.CheckAsync("bucket-b", "test@example.com", "newsletter");

        Assert.Equal(ConsentStatus.OptedOut, statusBucketA);
        Assert.Equal(ConsentStatus.OptedIn, statusBucketB);
    }

    private class InMemoryConsentRepository : IConsentRepository
    {
        private readonly Dictionary<string, ConsentRecord> _records = new();

        public Task<ConsentRecord?> GetAsync(string bucket, string emailHash, string permission)
        {
            var key = $"{bucket}:{emailHash}:{permission}";
            _records.TryGetValue(key, out var record);
            return Task.FromResult(record);
        }

        public Task UpsertAsync(ConsentRecord record)
        {
            var key = $"{record.Bucket}:{record.EmailHash}:{record.Permission}";
            _records[key] = record;
            return Task.CompletedTask;
        }

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
            {
                _records.Remove(key);
            }

            return Task.FromResult(keysToRemove.Count);
        }

        public Task<int> DeleteBucketAsync(string bucket)
        {
            var keysToRemove = _records
                .Where(kv => kv.Value.Bucket == bucket)
                .Select(kv => kv.Key)
                .ToList();

            foreach (var key in keysToRemove)
            {
                _records.Remove(key);
            }

            return Task.FromResult(keysToRemove.Count);
        }

        public Task<int> DeletePermissionAsync(string bucket, string permission)
        {
            var keysToRemove = _records
                .Where(kv => kv.Value.Bucket == bucket && kv.Value.Permission == permission)
                .Select(kv => kv.Key)
                .ToList();

            foreach (var key in keysToRemove)
            {
                _records.Remove(key);
            }

            return Task.FromResult(keysToRemove.Count);
        }

        public Task<bool> EmailExistsInBucketAsync(string bucket, string emailHash)
        {
            var exists = _records.Values.Any(r => r.Bucket == bucket && r.EmailHash == emailHash);
            return Task.FromResult(exists);
        }

        public Task<IReadOnlyList<ConsentRecord>> GetByEmailAsync(string bucket, string emailHash)
        {
            var records = _records.Values
                .Where(r => r.Bucket == bucket && r.EmailHash == emailHash)
                .ToList();
            return Task.FromResult<IReadOnlyList<ConsentRecord>>(records);
        }

        public Task<PagedResult<IdentityInfo>> GetIdentitiesAsync(int page, int pageSize, string? sortBy = null, string? sortDir = null, string? search = null) => throw new NotImplementedException();

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
                EncryptedEmail = _records.Values.FirstOrDefault(r => r.EmailHash == emailHash && r.EncryptedEmail != null)?.EncryptedEmail,
                Subscriptions = subs
            };

            return Task.FromResult<IdentityDetails?>(details);
        }

        public Task<IDisposable> BeginTransactionAsync() => Task.FromResult<IDisposable>(new NoOpDisposable());
        public Task CommitTransactionAsync() => Task.CompletedTask;

        private class NoOpDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }
}
