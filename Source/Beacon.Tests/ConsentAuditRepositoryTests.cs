using Microsoft.Extensions.DependencyInjection;
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
    public async Task ProcessOptOutAsync_AdminLink_AuditsSourceAndActor()
    {
        var service = new ConsentService(_repository, new NullBeaconCacheService(),
            new Beacon.Tests.Fakes.StubSystemConfigurationService(),
            new Beacon.Core.Security.EmailHasher("test-pepper"),
            new Beacon.Core.Security.Encryptor("MDEyMzQ1Njc4OTAxMjM0NTY3ODkwMTIzNDU2Nzg5MDE="));

        await service.ProcessOptOutAsync("bucket-a", "a@example.com", ["newsletter"], "tok", ConsentSource.AdminLink, actorId: "admin-1");
        _db.ChangeTracker.Clear();

        var entry = Assert.Single((await _repository.GetAuditAsync(null, null, 1, 10, TestContext.Current.CancellationToken)).Records);
        Assert.Equal(ConsentSource.AdminLink, entry.Source);
        Assert.Equal("admin-1", entry.ActorId);
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
        Assert.Equal(1, await _repository.AnonymiseOptedOutAsync(cutoff, "test", TestContext.Current.CancellationToken));
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

        Assert.Equal(0, await _repository.AnonymiseOptedOutAsync(DateTime.UtcNow.AddMinutes(1), "test", TestContext.Current.CancellationToken));
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

    [Fact]
    public async Task ExportIdentities_BucketFilter_ReturnsOnlyThatBucket()
    {
        var a = new string('a', 64);
        var b = new string('b', 64);
        await _repository.UpsertAsync(MakeRecord("bucket-a", a, "newsletter", ConsentStatus.OptedIn));
        await _repository.UpsertAsync(MakeRecord("bucket-b", a, "newsletter", ConsentStatus.OptedIn));
        await _repository.UpsertAsync(MakeRecord("bucket-b", b, "newsletter", ConsentStatus.OptedIn));
        _db.ChangeTracker.Clear();

        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider()
        };
        var encryptor = new Beacon.Core.Security.Encryptor("MDEyMzQ1Njc4OTAxMjM0NTY3ODkwMTIzNDU2Nzg5MDE=");
        var ct = TestContext.Current.CancellationToken;

        var full = await Beacon.Api.AdminEndpoints.ExportIdentities(new(Full: true, Bucket: "Bucket-A"), _repository, encryptor, context, ct);
        var fullCsv = Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.ContentHttpResult>(full).ResponseContent!;
        Assert.Contains("bucket-a", fullCsv);
        Assert.DoesNotContain("bucket-b", fullCsv);

        var summary = await Beacon.Api.AdminEndpoints.ExportIdentities(new(Bucket: "bucket-a"), _repository, encryptor, context, ct);
        var summaryCsv = Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.ContentHttpResult>(summary).ResponseContent!;
        Assert.Contains(a, summaryCsv);
        Assert.DoesNotContain(b, summaryCsv);
        Assert.Equal("1", context.Response.Headers["X-Total-Count"].ToString());

        var selected = await Beacon.Api.AdminEndpoints.ExportIdentities(new(Hashes: [a.ToUpperInvariant(), b], Bucket: "bucket-a"), _repository, encryptor, context, ct);
        var selectedCsv = Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.ContentHttpResult>(selected).ResponseContent!;
        Assert.Contains(a, selectedCsv);
        Assert.DoesNotContain(b, selectedCsv);
    }

    [Fact]
    public async Task BulkReads_CancelledToken_Throw()
    {
        await _repository.UpsertAsync(MakeRecord("bucket-a", new string('a', 64), "newsletter", ConsentStatus.OptedIn));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _repository.GetAllBucketRecordsAsync("bucket-a", cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _repository.GetIdentitiesAsync(1, 10, ct: cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _repository.GetIdentitiesByHashesAsync([new string('a', 64)], 1, 10, ct: cts.Token));
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
