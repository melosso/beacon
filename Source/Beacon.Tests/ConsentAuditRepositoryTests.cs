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

        var result = await _repository.GetAuditAsync(null, null, 1, 10, TestContext.Current.CancellationToken);
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

        var result = await _repository.GetAuditAsync(null, null, 1, 10, TestContext.Current.CancellationToken);
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

        var result = await _repository.GetAuditAsync(null, null, 1, 10, TestContext.Current.CancellationToken);
        Assert.Equal(1, result.Total);
    }

    [Fact]
    public async Task UpsertAsync_WithActorId_StoresActorInAuditEntry()
    {
        var record = MakeRecord("bucket-a", "hash1", "newsletter", ConsentStatus.OptedIn);

        await _repository.UpsertAsync(record, "alice");
        _db.ChangeTracker.Clear();

        var result = await _repository.GetAuditAsync(null, null, 1, 10, TestContext.Current.CancellationToken);
        Assert.Equal("alice", result.Records[0].ActorId);
    }

    [Fact]
    public async Task GetAuditAsync_Pagination_ReturnsCorrectPage()
    {
        await _repository.UpsertAsync(MakeRecord("bucket-a", "hash1", "newsletter", ConsentStatus.OptedIn));
        await _repository.UpsertAsync(MakeRecord("bucket-a", "hash2", "newsletter", ConsentStatus.OptedIn));
        await _repository.UpsertAsync(MakeRecord("bucket-a", "hash3", "newsletter", ConsentStatus.OptedIn));
        _db.ChangeTracker.Clear();

        var page1 = await _repository.GetAuditAsync(null, null, 1, 2, TestContext.Current.CancellationToken);
        var page2 = await _repository.GetAuditAsync(null, null, 2, 2, TestContext.Current.CancellationToken);

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

        var result = await _repository.GetAuditAsync("bucket-a", null, 1, 10, TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Total);
        Assert.Equal("bucket-a", result.Records[0].Bucket);
    }

    [Fact]
    public async Task GetAuditAsync_EmailHashFilter_ReturnsOnlyMatchingIdentity()
    {
        await _repository.UpsertAsync(MakeRecord("bucket-a", "hash1", "newsletter", ConsentStatus.OptedIn));
        await _repository.UpsertAsync(MakeRecord("bucket-a", "hash2", "newsletter", ConsentStatus.OptedIn));
        _db.ChangeTracker.Clear();

        var result = await _repository.GetAuditAsync(null, "hash2", 1, 10, TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Total);
        Assert.Equal("hash2", result.Records[0].EmailHash);
    }

    [Fact]
    public async Task AnonymiseOptedOut_WipesNameAndAuditCustomFields()
    {
        var record = MakeRecord("bucket-a", "hash1", "newsletter", ConsentStatus.OptedOut);
        record.EncryptedEmail = "enc-email";
        record.EncryptedName = "enc-name";
        record.CustomFields = """{"city":"Utrecht"}""";
        await _repository.UpsertAsync(record);
        _db.ChangeTracker.Clear();

        var cutoff = DateTime.UtcNow.AddMinutes(1);
        Assert.Equal(1, await _repository.CountOptedOutToAnonymiseAsync(cutoff, TestContext.Current.CancellationToken));
        Assert.Equal(1, await _repository.AnonymiseOptedOutAsync(cutoff, TestContext.Current.CancellationToken));
        _db.ChangeTracker.Clear();

        var stored = await _db.ConsentRecords.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Null(stored.EncryptedName);
        Assert.Null(stored.EncryptedEmail);
        Assert.Equal(ConsentStatus.OptedOut, stored.Status);
        Assert.All(await _db.ConsentAuditEntries.ToListAsync(TestContext.Current.CancellationToken), e => Assert.Null(e.CustomFields));
        Assert.Equal(0, await _repository.CountOptedOutToAnonymiseAsync(cutoff, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AnonymiseOptedOut_KeepsOptedInAuditCustomFields()
    {
        var record = MakeRecord("bucket-a", "hash1", "newsletter", ConsentStatus.OptedIn);
        record.EncryptedEmail = "enc-email";
        record.CustomFields = """{"city":"Utrecht"}""";
        await _repository.UpsertAsync(record);
        _db.ChangeTracker.Clear();

        Assert.Equal(0, await _repository.AnonymiseOptedOutAsync(DateTime.UtcNow.AddMinutes(1), TestContext.Current.CancellationToken));
        _db.ChangeTracker.Clear();

        Assert.NotNull((await _db.ConsentAuditEntries.SingleAsync(TestContext.Current.CancellationToken)).CustomFields);
    }

    [Fact]
    public async Task DeleteRecordAsync_FailedErasureAudit_KeepsRecord()
    {
        await _repository.UpsertAsync(MakeRecord("bucket-a", "hash1", "newsletter", ConsentStatus.OptedIn));
        _db.ChangeTracker.Clear();

        var failing = new FailingSaveInterceptor();
        await using (var db = new BeaconDbContext(new DbContextOptionsBuilder<BeaconDbContext>().UseSqlite(_connection).AddInterceptors(failing).Options))
        {
            failing.Armed = true;
            await Assert.ThrowsAsync<DbUpdateException>(() => new ConsentRepository(db).DeleteRecordAsync("bucket-a", "hash1", "ops"));
        }

        Assert.Equal(1, await _db.ConsentRecords.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await _db.ConsentAuditEntries.CountAsync(TestContext.Current.CancellationToken));
    }

    private sealed class FailingSaveInterceptor : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        public bool Armed { get; set; }

        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,
            CancellationToken cancellationToken = default) =>
            Armed ? throw new DbUpdateException("Simulated audit write failure.") : base.SavingChangesAsync(eventData, result, cancellationToken);
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
