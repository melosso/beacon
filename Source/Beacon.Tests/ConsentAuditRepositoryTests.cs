using Beacon.Core.Models;
using Beacon.Core.Services;
using Beacon.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Beacon.Tests;

public class ConsentAuditRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly BeaconDbContext _db;
    private readonly ConsentRepository _repository;

    public ConsentAuditRepositoryTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<BeaconDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new BeaconDbContext(options);
        _db.Database.EnsureCreated();
        _repository = new ConsentRepository(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task UpsertAsync_Insert_CreatesAuditEntryWithNullOldStatus()
    {
        var record = MakeRecord("bucket-a", "hash1", "newsletter", ConsentStatus.OptedIn);

        await _repository.UpsertAsync(record);
        _db.ChangeTracker.Clear();

        var result = await _repository.GetAuditAsync(null, null, 1, 10);
        Assert.Equal(1, result.Total);
        var entry = result.Records[0];
        Assert.Null(entry.OldStatus);
        Assert.Equal(ConsentStatus.OptedIn, entry.NewStatus);
        Assert.Equal("bucket-a", entry.Bucket);
        Assert.Equal("hash1", entry.EmailHash);
        Assert.Equal("newsletter", entry.Permission);
    }

    [Fact]
    public async Task UpsertAsync_StatusChange_CreatesSecondAuditEntry()
    {
        await _repository.UpsertAsync(MakeRecord("bucket-a", "hash1", "newsletter", ConsentStatus.OptedIn));
        _db.ChangeTracker.Clear();

        await _repository.UpsertAsync(MakeRecord("bucket-a", "hash1", "newsletter", ConsentStatus.OptedOut));
        _db.ChangeTracker.Clear();

        var result = await _repository.GetAuditAsync(null, null, 1, 10);
        Assert.Equal(2, result.Total);

        var latest = result.Records[0];
        Assert.Equal(ConsentStatus.OptedIn, latest.OldStatus);
        Assert.Equal(ConsentStatus.OptedOut, latest.NewStatus);
    }

    [Fact]
    public async Task UpsertAsync_SameStatus_NoNewAuditEntry()
    {
        var record = MakeRecord("bucket-a", "hash1", "newsletter", ConsentStatus.OptedIn);
        await _repository.UpsertAsync(record);

        await _repository.UpsertAsync(record);
        _db.ChangeTracker.Clear();

        var result = await _repository.GetAuditAsync(null, null, 1, 10);
        Assert.Equal(1, result.Total);
    }

    [Fact]
    public async Task UpsertAsync_WithActorId_StoresActorInAuditEntry()
    {
        var record = MakeRecord("bucket-a", "hash1", "newsletter", ConsentStatus.OptedIn);

        await _repository.UpsertAsync(record, "alice");
        _db.ChangeTracker.Clear();

        var result = await _repository.GetAuditAsync(null, null, 1, 10);
        Assert.Equal("alice", result.Records[0].ActorId);
    }

    [Fact]
    public async Task GetAuditAsync_Pagination_ReturnsCorrectPage()
    {
        await _repository.UpsertAsync(MakeRecord("bucket-a", "hash1", "newsletter", ConsentStatus.OptedIn));
        await _repository.UpsertAsync(MakeRecord("bucket-a", "hash2", "newsletter", ConsentStatus.OptedIn));
        await _repository.UpsertAsync(MakeRecord("bucket-a", "hash3", "newsletter", ConsentStatus.OptedIn));
        _db.ChangeTracker.Clear();

        var page1 = await _repository.GetAuditAsync(null, null, 1, 2);
        var page2 = await _repository.GetAuditAsync(null, null, 2, 2);

        Assert.Equal(3, page1.Total);
        Assert.Equal(2, page1.Records.Count);
        Assert.Single(page2.Records);
    }

    [Fact]
    public async Task GetAuditAsync_BucketFilter_ReturnsOnlyMatchingBucket()
    {
        await _repository.UpsertAsync(MakeRecord("bucket-a", "hash1", "newsletter", ConsentStatus.OptedIn));
        await _repository.UpsertAsync(MakeRecord("bucket-b", "hash1", "newsletter", ConsentStatus.OptedIn));
        _db.ChangeTracker.Clear();

        var result = await _repository.GetAuditAsync("bucket-a", null, 1, 10);

        Assert.Equal(1, result.Total);
        Assert.Equal("bucket-a", result.Records[0].Bucket);
    }

    [Fact]
    public async Task GetAuditAsync_EmailHashFilter_ReturnsOnlyMatchingIdentity()
    {
        await _repository.UpsertAsync(MakeRecord("bucket-a", "hash1", "newsletter", ConsentStatus.OptedIn));
        await _repository.UpsertAsync(MakeRecord("bucket-a", "hash2", "newsletter", ConsentStatus.OptedIn));
        _db.ChangeTracker.Clear();

        var result = await _repository.GetAuditAsync(null, "hash2", 1, 10);

        Assert.Equal(1, result.Total);
        Assert.Equal("hash2", result.Records[0].EmailHash);
    }

    private static ConsentRecord MakeRecord(string bucket, string emailHash, string permission, ConsentStatus status) =>
        new()
        {
            Id = Guid.NewGuid(),
            Bucket = bucket,
            EmailHash = emailHash,
            Permission = permission,
            Status = status,
            Source = ConsentSource.Admin,
            ChangedAt = DateTime.UtcNow
        };
}
