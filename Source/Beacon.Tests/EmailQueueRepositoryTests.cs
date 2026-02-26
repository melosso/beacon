using Beacon.Core.Models;
using Beacon.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Beacon.Tests;

public class EmailQueueRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly BeaconDbContext _db;
    private readonly EmailQueueRepository _repository;

    public EmailQueueRepositoryTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<BeaconDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new BeaconDbContext(options);
        _db.Database.EnsureCreated();
        _repository = new EmailQueueRepository(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private static EmailQueueEntry MakeEntry(
        string token,
        string bucket = "test-bucket",
        string emailHash = "abc123",
        string permission = "newsletter",
        DateTime? expiresAt = null) => new()
    {
        Bucket = bucket,
        EncryptedEmail = "encrypted-blob",
        EmailHash = emailHash,
        Permission = permission,
        Language = "en",
        ConfirmationToken = token,
        ConfirmationUrl = $"https://example.com/confirm/{token}",
        ExpiresAt = expiresAt ?? DateTime.UtcNow.AddDays(7)
    };

    // ── EnqueueAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task EnqueueAsync_AddsEntryWithPendingStatus()
    {
        var entry = MakeEntry("tk1");
        await _repository.EnqueueAsync(entry);

        var stored = await _db.EmailQueueEntries.FindAsync(entry.Id);
        Assert.NotNull(stored);
        Assert.Equal(EmailQueueStatus.Pending, stored.Status);
    }

    [Fact]
    public async Task EnqueueAsync_PreservesAllFields()
    {
        var entry = MakeEntry("tk1", bucket: "my-bucket", emailHash: "hash42", permission: "alerts");
        await _repository.EnqueueAsync(entry);

        var stored = await _db.EmailQueueEntries.FindAsync(entry.Id);
        Assert.NotNull(stored);
        Assert.Equal("my-bucket", stored.Bucket);
        Assert.Equal("hash42", stored.EmailHash);
        Assert.Equal("alerts", stored.Permission);
        Assert.Equal("en", stored.Language);
        Assert.Equal("tk1", stored.ConfirmationToken);
    }

    // ── GetPendingBatchAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task GetPendingBatchAsync_ReturnsPendingEntries()
    {
        await _repository.EnqueueAsync(MakeEntry("tk1", emailHash: "h1"));
        await _repository.EnqueueAsync(MakeEntry("tk2", emailHash: "h2"));

        var batch = await _repository.GetPendingBatchAsync();

        Assert.Equal(2, batch.Count);
    }

    [Fact]
    public async Task GetPendingBatchAsync_ExcludesExpiredEntries()
    {
        await _repository.EnqueueAsync(MakeEntry("live", emailHash: "h1", expiresAt: DateTime.UtcNow.AddDays(1)));
        await _repository.EnqueueAsync(MakeEntry("exp", emailHash: "h2", expiresAt: DateTime.UtcNow.AddSeconds(-1)));

        var batch = await _repository.GetPendingBatchAsync();

        Assert.Single(batch);
        Assert.Equal("live", batch[0].ConfirmationToken);
    }

    [Fact]
    public async Task GetPendingBatchAsync_ExcludesEntriesWithFutureNextAttempt()
    {
        var entry = MakeEntry("tk1");
        await _repository.EnqueueAsync(entry);
        await _repository.MarkFailedAsync(entry.Id, "err", DateTime.UtcNow.AddMinutes(10));

        var batch = await _repository.GetPendingBatchAsync();

        Assert.Empty(batch);
    }

    [Fact]
    public async Task GetPendingBatchAsync_RespectsBatchSizeLimit()
    {
        for (int i = 0; i < 5; i++)
            await _repository.EnqueueAsync(MakeEntry($"tk{i}", emailHash: $"h{i}"));

        var batch = await _repository.GetPendingBatchAsync(batchSize: 3);

        Assert.Equal(3, batch.Count);
    }

    [Fact]
    public async Task GetPendingBatchAsync_ReturnsEntriesOrderedByCreatedAt()
    {
        await _repository.EnqueueAsync(MakeEntry("first", emailHash: "h1"));
        await _repository.EnqueueAsync(MakeEntry("second", emailHash: "h2"));

        var batch = await _repository.GetPendingBatchAsync();

        Assert.Equal("first", batch[0].ConfirmationToken);
        Assert.Equal("second", batch[1].ConfirmationToken);
    }

    // ── MarkSentAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task MarkSentAsync_SetsSentStatus()
    {
        var entry = MakeEntry("tk1");
        await _repository.EnqueueAsync(entry);

        await _repository.MarkSentAsync(entry.Id, DateTime.UtcNow);
        _db.ChangeTracker.Clear();

        var updated = await _db.EmailQueueEntries.FindAsync(entry.Id);
        Assert.Equal(EmailQueueStatus.Sent, updated!.Status);
    }

    [Fact]
    public async Task MarkSentAsync_IncrementsAttemptCount()
    {
        var entry = MakeEntry("tk1");
        await _repository.EnqueueAsync(entry);

        await _repository.MarkSentAsync(entry.Id, DateTime.UtcNow);
        _db.ChangeTracker.Clear();

        var updated = await _db.EmailQueueEntries.FindAsync(entry.Id);
        Assert.Equal(1, updated!.AttemptCount);
    }

    [Fact]
    public async Task MarkSentAsync_SetsSentAt()
    {
        var entry = MakeEntry("tk1");
        await _repository.EnqueueAsync(entry);
        var before = DateTime.UtcNow;

        await _repository.MarkSentAsync(entry.Id, DateTime.UtcNow);
        _db.ChangeTracker.Clear();

        var updated = await _db.EmailQueueEntries.FindAsync(entry.Id);
        Assert.NotNull(updated!.SentAt);
        Assert.True(updated.SentAt >= before);
    }

    // ── MarkFailedAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task MarkFailedAsync_WithNextAttempt_RemainsPending()
    {
        var entry = MakeEntry("tk1");
        await _repository.EnqueueAsync(entry);

        await _repository.MarkFailedAsync(entry.Id, "timeout", DateTime.UtcNow.AddMinutes(5));
        _db.ChangeTracker.Clear();

        var updated = await _db.EmailQueueEntries.FindAsync(entry.Id);
        Assert.Equal(EmailQueueStatus.Pending, updated!.Status);
    }

    [Fact]
    public async Task MarkFailedAsync_WithoutNextAttempt_SetsFailed()
    {
        var entry = MakeEntry("tk1");
        await _repository.EnqueueAsync(entry);

        await _repository.MarkFailedAsync(entry.Id, "unrecoverable", null);
        _db.ChangeTracker.Clear();

        var updated = await _db.EmailQueueEntries.FindAsync(entry.Id);
        Assert.Equal(EmailQueueStatus.Failed, updated!.Status);
    }

    [Fact]
    public async Task MarkFailedAsync_SetsLastError()
    {
        var entry = MakeEntry("tk1");
        await _repository.EnqueueAsync(entry);

        await _repository.MarkFailedAsync(entry.Id, "SMTP refused", null);
        _db.ChangeTracker.Clear();

        var updated = await _db.EmailQueueEntries.FindAsync(entry.Id);
        Assert.Equal("SMTP refused", updated!.LastError);
    }

    [Fact]
    public async Task MarkFailedAsync_IncrementsAttemptCount()
    {
        var entry = MakeEntry("tk1");
        await _repository.EnqueueAsync(entry);

        await _repository.MarkFailedAsync(entry.Id, "err", null);
        _db.ChangeTracker.Clear();

        var updated = await _db.EmailQueueEntries.FindAsync(entry.Id);
        Assert.Equal(1, updated!.AttemptCount);
    }

    // ── GetByConfirmationTokenAsync ──────────────────────────────────────────

    [Fact]
    public async Task GetByConfirmationTokenAsync_ReturnsEntry_WhenTokenExists()
    {
        await _repository.EnqueueAsync(MakeEntry("secret-token"));

        var result = await _repository.GetByConfirmationTokenAsync("secret-token");

        Assert.NotNull(result);
        Assert.Equal("secret-token", result.ConfirmationToken);
    }

    [Fact]
    public async Task GetByConfirmationTokenAsync_ReturnsNull_WhenTokenNotFound()
    {
        var result = await _repository.GetByConfirmationTokenAsync("no-such-token");

        Assert.Null(result);
    }

    // ── MarkConfirmedAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task MarkConfirmedAsync_SetsConfirmedStatus()
    {
        var entry = MakeEntry("tk1");
        await _repository.EnqueueAsync(entry);

        await _repository.MarkConfirmedAsync(entry.Id, DateTime.UtcNow);
        _db.ChangeTracker.Clear();

        var updated = await _db.EmailQueueEntries.FindAsync(entry.Id);
        Assert.Equal(EmailQueueStatus.Confirmed, updated!.Status);
    }

    [Fact]
    public async Task MarkConfirmedAsync_SetsConfirmedAt()
    {
        var entry = MakeEntry("tk1");
        await _repository.EnqueueAsync(entry);
        var before = DateTime.UtcNow;

        await _repository.MarkConfirmedAsync(entry.Id, DateTime.UtcNow);
        _db.ChangeTracker.Clear();

        var updated = await _db.EmailQueueEntries.FindAsync(entry.Id);
        Assert.NotNull(updated!.ConfirmedAt);
        Assert.True(updated.ConfirmedAt >= before);
    }

    // ── HasPendingAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task HasPendingAsync_ReturnsTrue_ForPendingEntry()
    {
        await _repository.EnqueueAsync(MakeEntry("tk", bucket: "b", emailHash: "h", permission: "p"));

        Assert.True(await _repository.HasPendingAsync("b", "h", "p"));
    }

    [Fact]
    public async Task HasPendingAsync_ReturnsTrue_ForSentEntry()
    {
        var entry = MakeEntry("tk", bucket: "b", emailHash: "h", permission: "p");
        await _repository.EnqueueAsync(entry);
        await _repository.MarkSentAsync(entry.Id, DateTime.UtcNow);

        Assert.True(await _repository.HasPendingAsync("b", "h", "p"));
    }

    [Fact]
    public async Task HasPendingAsync_ReturnsFalse_WhenNoneExist()
    {
        Assert.False(await _repository.HasPendingAsync("b", "h", "p"));
    }

    [Fact]
    public async Task HasPendingAsync_ReturnsFalse_ForConfirmedEntry()
    {
        var entry = MakeEntry("tk", bucket: "b", emailHash: "h", permission: "p");
        await _repository.EnqueueAsync(entry);
        await _repository.MarkConfirmedAsync(entry.Id, DateTime.UtcNow);

        Assert.False(await _repository.HasPendingAsync("b", "h", "p"));
    }

    [Fact]
    public async Task HasPendingAsync_ReturnsFalse_ForFailedEntry()
    {
        var entry = MakeEntry("tk", bucket: "b", emailHash: "h", permission: "p");
        await _repository.EnqueueAsync(entry);
        await _repository.MarkFailedAsync(entry.Id, "err", null);

        Assert.False(await _repository.HasPendingAsync("b", "h", "p"));
    }

    // ── PruneExpiredAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task PruneExpiredAsync_MarksExpiredPendingEntries()
    {
        var entry = MakeEntry("tk1", expiresAt: DateTime.UtcNow.AddSeconds(-1));
        await _repository.EnqueueAsync(entry);

        await _repository.PruneExpiredAsync();
        _db.ChangeTracker.Clear();

        var updated = await _db.EmailQueueEntries.FindAsync(entry.Id);
        Assert.Equal(EmailQueueStatus.Expired, updated!.Status);
    }

    [Fact]
    public async Task PruneExpiredAsync_DoesNotAffectActiveEntries()
    {
        var entry = MakeEntry("tk1", expiresAt: DateTime.UtcNow.AddDays(7));
        await _repository.EnqueueAsync(entry);

        await _repository.PruneExpiredAsync();
        _db.ChangeTracker.Clear();

        var updated = await _db.EmailQueueEntries.FindAsync(entry.Id);
        Assert.Equal(EmailQueueStatus.Pending, updated!.Status);
    }

    [Fact]
    public async Task PruneExpiredAsync_DoesNotOverwriteConfirmedEntries()
    {
        var entry = MakeEntry("tk1", expiresAt: DateTime.UtcNow.AddSeconds(-1));
        await _repository.EnqueueAsync(entry);
        await _repository.MarkConfirmedAsync(entry.Id, DateTime.UtcNow);

        await _repository.PruneExpiredAsync();
        _db.ChangeTracker.Clear();

        var updated = await _db.EmailQueueEntries.FindAsync(entry.Id);
        Assert.Equal(EmailQueueStatus.Confirmed, updated!.Status);
    }
}
