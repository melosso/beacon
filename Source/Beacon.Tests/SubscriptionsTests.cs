using Beacon.Core.Models;
using Beacon.Core.Security;
using Beacon.Core.Services;
using Xunit;

namespace Beacon.Tests;

public class SubscriptionsTests
{
    private const string TestPepper = "test-pepper-for-hashing";
    private const string TestEncryptionKey = "MDEyMzQ1Njc4OTAxMjM0NTY3ODkwMTIzNDU2Nzg5MDE=";

    [Fact]
    public async Task GetIdentityDetailsAsync_ReturnsPermissionsAcrossBuckets()
    {
        // Arrange
        var repository = new InMemoryConsentRepository();
        var emailHasher = new EmailHasher(TestPepper);
        var encryptor = new Encryptor(TestEncryptionKey);
        var service = new ConsentService(repository, emailHasher, encryptor);

        var email = "user@example.com";
        var emailHash = emailHasher.Hash(email);

        // Bucket A
        await service.OverrideAsync("bucket-a", email, "news", ConsentStatus.OptedIn);
        await service.OverrideAsync("bucket-a", email, "marketing", ConsentStatus.OptedOut);

        // Bucket B
        await service.OverrideAsync("bucket-b", email, "alerts", ConsentStatus.OptedIn);

        // Act
        var details = await repository.GetIdentityDetailsAsync(emailHash);

        // Assert
        Assert.NotNull(details);
        Assert.Equal(2, details.Subscriptions.Count);
        
        var subA = details.Subscriptions.First(s => s.Bucket == "bucket-a");
        Assert.True(subA.Permissions["news"]);
        Assert.False(subA.Permissions["marketing"]);

        var subB = details.Subscriptions.First(s => s.Bucket == "bucket-b");
        Assert.True(subB.Permissions["alerts"]);
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

        public Task<IReadOnlyList<BucketInfo>> GetBucketsAsync() => throw new NotImplementedException();
        public Task<BucketDetails> GetBucketDetailsAsync(string bucket) => throw new NotImplementedException();
        public Task<PagedResult<EmailPermissions>> GetBucketRecordsAsync(string bucket, int page, int pageSize, string? sortBy = null, string? sortDir = null, string? search = null) => throw new NotImplementedException();
        public Task<IReadOnlyList<EmailPermissions>> GetAllBucketRecordsAsync(string bucket) => throw new NotImplementedException();
        public Task<int> DeleteBucketAsync(string bucket) => throw new NotImplementedException();
        public Task<int> DeletePermissionAsync(string bucket, string permission) => throw new NotImplementedException();
        public Task<int> DeleteRecordAsync(string bucket, string emailHash) => throw new NotImplementedException();
        public Task<bool> EmailExistsInBucketAsync(string bucket, string emailHash) => throw new NotImplementedException();
        public Task<IReadOnlyList<ConsentRecord>> GetByEmailAsync(string bucket, string emailHash) => throw new NotImplementedException();
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
