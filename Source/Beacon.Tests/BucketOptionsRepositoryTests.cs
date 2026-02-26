using Beacon.Core.Models;
using Beacon.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Beacon.Tests;

public class BucketOptionsRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly BeaconDbContext _db;
    private readonly BucketOptionsRepository _repository;

    public BucketOptionsRepositoryTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<BeaconDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new BeaconDbContext(options);
        _db.Database.EnsureCreated();
        _repository = new BucketOptionsRepository(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    // ── GetAsync ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAsync_ReturnsDefault_WhenNoBucketRecordExists()
    {
        var result = await _repository.GetAsync("unknown-bucket");

        Assert.NotNull(result);
        Assert.Equal("unknown-bucket", result.Bucket);
        Assert.True(result.DoubleOptIn);
    }

    [Fact]
    public async Task GetAsync_ReturnsStoredValue_WhenRecordExists()
    {
        await _repository.SaveAsync(new BucketOptions { Bucket = "my-bucket", DoubleOptIn = false });

        var result = await _repository.GetAsync("my-bucket");

        Assert.False(result.DoubleOptIn);
    }

    [Fact]
    public async Task GetAsync_IsolatesBuckets()
    {
        await _repository.SaveAsync(new BucketOptions { Bucket = "bucket-a", DoubleOptIn = false });

        var a = await _repository.GetAsync("bucket-a");
        var b = await _repository.GetAsync("bucket-b");

        Assert.False(a.DoubleOptIn);
        Assert.True(b.DoubleOptIn);  // default
    }

    // ── SaveAsync ────────────────────────────────────────────────────────────

    [Fact]
    public async Task SaveAsync_InsertsNewRecord()
    {
        await _repository.SaveAsync(new BucketOptions { Bucket = "new-bucket", DoubleOptIn = false });

        var count = await _db.BucketOptions.CountAsync(b => b.Bucket == "new-bucket");
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task SaveAsync_SetsUpdatedAt_OnInsert()
    {
        var before = DateTime.UtcNow;
        await _repository.SaveAsync(new BucketOptions { Bucket = "my-bucket", DoubleOptIn = true });
        var after = DateTime.UtcNow;

        var stored = await _db.BucketOptions.FindAsync("my-bucket");
        Assert.NotNull(stored!.UpdatedAt);
        Assert.InRange(stored.UpdatedAt!.Value, before, after);
    }

    [Fact]
    public async Task SaveAsync_UpdatesExistingRecord()
    {
        await _repository.SaveAsync(new BucketOptions { Bucket = "my-bucket", DoubleOptIn = true });
        await _repository.SaveAsync(new BucketOptions { Bucket = "my-bucket", DoubleOptIn = false });

        var result = await _repository.GetAsync("my-bucket");
        Assert.False(result.DoubleOptIn);
    }

    [Fact]
    public async Task SaveAsync_DoesNotDuplicateRecord_OnUpdate()
    {
        await _repository.SaveAsync(new BucketOptions { Bucket = "my-bucket", DoubleOptIn = true });
        await _repository.SaveAsync(new BucketOptions { Bucket = "my-bucket", DoubleOptIn = false });

        var count = await _db.BucketOptions.CountAsync(b => b.Bucket == "my-bucket");
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task SaveAsync_SetsUpdatedAt_OnUpdate()
    {
        await _repository.SaveAsync(new BucketOptions { Bucket = "my-bucket", DoubleOptIn = true });
        var before = DateTime.UtcNow;

        await _repository.SaveAsync(new BucketOptions { Bucket = "my-bucket", DoubleOptIn = false });
        var after = DateTime.UtcNow;

        var stored = await _db.BucketOptions.FindAsync("my-bucket");
        Assert.NotNull(stored!.UpdatedAt);
        Assert.InRange(stored.UpdatedAt!.Value, before, after);
    }

    [Fact]
    public async Task SaveAsync_RoundTrip_DoubleOptInTrue()
    {
        await _repository.SaveAsync(new BucketOptions { Bucket = "bucket", DoubleOptIn = true });

        var result = await _repository.GetAsync("bucket");
        Assert.True(result.DoubleOptIn);
    }

    [Fact]
    public async Task SaveAsync_RoundTrip_DoubleOptInFalse()
    {
        await _repository.SaveAsync(new BucketOptions { Bucket = "bucket", DoubleOptIn = false });

        var result = await _repository.GetAsync("bucket");
        Assert.False(result.DoubleOptIn);
    }
}
