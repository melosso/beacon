using Beacon.Core.Services;
using Beacon.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Beacon.Tests;

public class BucketArchiveTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly BeaconDbContext _db;
    private readonly BucketRepository _repository;

    public BucketArchiveTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<BeaconDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new BeaconDbContext(options);
        _db.Database.EnsureCreated();
        _repository = new BucketRepository(_db, new NullBeaconCacheService(), new Beacon.Tests.Fakes.StubSystemConfigurationService());
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task IsArchivedAsync_ReturnsFalse_WhenBucketNotArchived()
    {
        var result = await _repository.IsArchivedAsync("test-bucket");

        Assert.False(result);
    }

    [Fact]
    public async Task ArchiveAsync_SetsBucketAsArchived()
    {
        await _repository.ArchiveAsync("test-bucket");

        var isArchived = await _repository.IsArchivedAsync("test-bucket");
        Assert.True(isArchived);
    }

    [Fact]
    public async Task ArchiveAsync_SetsArchivedAtTimestamp()
    {
        var before = DateTime.UtcNow;
        await _repository.ArchiveAsync("test-bucket");
        var after = DateTime.UtcNow;

        var entry = await _db.ArchivedBuckets.FindAsync("test-bucket");
        Assert.NotNull(entry);
        Assert.InRange(entry.ArchivedAt, before, after);
    }

    [Fact]
    public async Task ArchiveAsync_IsIdempotent()
    {
        await _repository.ArchiveAsync("test-bucket");
        await _repository.ArchiveAsync("test-bucket");

        var count = await _db.ArchivedBuckets.CountAsync(a => a.Bucket == "test-bucket");
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task UnarchiveAsync_RemovesArchivedState()
    {
        await _repository.ArchiveAsync("test-bucket");
        Assert.True(await _repository.IsArchivedAsync("test-bucket"));

        await _repository.UnarchiveAsync("test-bucket");

        Assert.False(await _repository.IsArchivedAsync("test-bucket"));
    }

    [Fact]
    public async Task UnarchiveAsync_IsNoOp_WhenNotArchived()
    {
        // Should not throw
        await _repository.UnarchiveAsync("nonexistent-bucket");

        var count = await _db.ArchivedBuckets.CountAsync();
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task ArchiveAsync_IsolatesBuckets()
    {
        await _repository.ArchiveAsync("bucket-a");

        Assert.True(await _repository.IsArchivedAsync("bucket-a"));
        Assert.False(await _repository.IsArchivedAsync("bucket-b"));
    }

    [Fact]
    public async Task ArchiveAndUnarchive_FullCycle()
    {
        Assert.False(await _repository.IsArchivedAsync("test-bucket"));

        await _repository.ArchiveAsync("test-bucket");
        Assert.True(await _repository.IsArchivedAsync("test-bucket"));

        await _repository.UnarchiveAsync("test-bucket");
        Assert.False(await _repository.IsArchivedAsync("test-bucket"));

        // Can re-archive after unarchive
        await _repository.ArchiveAsync("test-bucket");
        Assert.True(await _repository.IsArchivedAsync("test-bucket"));
    }
}
