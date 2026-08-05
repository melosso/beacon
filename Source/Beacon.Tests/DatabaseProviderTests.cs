using Beacon.Core.Models;
using Beacon.Storage;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;
using Testcontainers.MySql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Beacon.Tests;

/// <summary>
/// Abstract base — shared test logic executed against every database provider.
/// Concrete subclasses supply the provider-specific DbContext.
/// </summary>
public abstract class DatabaseProviderTests
{
    protected abstract BeaconDbContext CreateDbContext();

    private static ConsentRecord MakeRecord(
        string bucket, string hash, string perm, ConsentStatus status,
        string? encEmail = null, string? encName = null) => new()
    {
        Id = Guid.NewGuid(),
        Bucket = bucket,
        EmailHash = hash,
        Permission = perm,
        Status = status,
        Source = ConsentSource.Api,
        ChangedAt = DateTime.UtcNow,
        EncryptedEmail = encEmail,
        EncryptedName = encName
    };

    [Fact]
    public void DatabaseMigrator_Initialize_DoesNotThrow()
    {
        using var db = CreateDbContext();
        // Non-SQLite providers return after EnsureCreated; SQLite runs the full migrator.
        DatabaseMigrator.Initialize(db);
    }

    [Fact]
    public async Task GetBucketDetailsAsync_ReturnsCorrectOptInOutCounts()
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        var bucket = $"bucket-{id}";

        await using var db = CreateDbContext();
        db.ConsentRecords.AddRange(
            MakeRecord(bucket, "h1", "newsletter", ConsentStatus.OptedIn),
            MakeRecord(bucket, "h2", "newsletter", ConsentStatus.OptedIn),
            MakeRecord(bucket, "h3", "newsletter", ConsentStatus.OptedOut),
            MakeRecord(bucket, "h1", "sms", ConsentStatus.OptedOut));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var repo = new ConsentRepository(db);
        var details = await repo.GetBucketDetailsAsync(bucket);

        Assert.Equal(bucket, details.Name);
        Assert.Equal(2, details.Permissions.Count);

        var newsletter = details.Stats.Single(s => s.Permission == "newsletter");
        Assert.Equal(2, newsletter.OptedIn);
        Assert.Equal(1, newsletter.OptedOut);

        var sms = details.Stats.Single(s => s.Permission == "sms");
        Assert.Equal(0, sms.OptedIn);
        Assert.Equal(1, sms.OptedOut);
    }

    [Fact]
    public async Task GetIdentityDetailsAsync_ReturnsBothEncryptedFields_FromSingleLoad()
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        var hash = $"hash-{id}";

        await using var db = CreateDbContext();
        // EncryptedEmail on one row, EncryptedName on a different row for the same identity.
        db.ConsentRecords.AddRange(
            MakeRecord("bucket-a", hash, "p1", ConsentStatus.OptedIn, encEmail: "enc-email"),
            MakeRecord("bucket-a", hash, "p2", ConsentStatus.OptedIn, encName: "enc-name"));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var repo = new ConsentRepository(db);
        var identity = await repo.GetIdentityDetailsAsync(hash);

        Assert.NotNull(identity);
        Assert.Equal(hash, identity.EmailHash);
        Assert.Equal("enc-email", identity.EncryptedEmail);
        Assert.Equal("enc-name", identity.EncryptedName);
        Assert.Single(identity.Subscriptions);
        Assert.Equal("bucket-a", identity.Subscriptions[0].Bucket);
        Assert.Equal(2, identity.Subscriptions[0].Permissions.Count);
    }

    [Fact]
    public async Task GetIdentityDetailsAsync_ReturnsNull_WhenHashNotFound()
    {
        await using var db = CreateDbContext();
        var repo = new ConsentRepository(db);

        var result = await repo.GetIdentityDetailsAsync("nonexistent-hash-" + Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task UpsertAsync_ThenGetAsync_RoundTrips()
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        var bucket = $"rt-{id}";

        await using var db = CreateDbContext();
        var repo = new ConsentRepository(db);

        var record = MakeRecord(bucket, "hash1", "email", ConsentStatus.OptedIn);
        await repo.UpsertAsync(record);
        db.ChangeTracker.Clear();

        var result = await repo.GetAsync(bucket, "hash1", "email");

        Assert.NotNull(result);
        Assert.Equal(ConsentStatus.OptedIn, result.Status);
    }

    [Fact]
    public async Task GetBucketDetailsAsync_ReturnsEmptyDetails_WhenBucketHasNoRecords()
    {
        await using var db = CreateDbContext();
        var repo = new ConsentRepository(db);

        var details = await repo.GetBucketDetailsAsync("empty-bucket-" + Guid.NewGuid());

        Assert.Empty(details.Stats);
        Assert.Empty(details.Permissions);
    }
}

// ─── SQL Server ────────────────────────────────────────────────────────────────

public class SqlServerContainerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
    public string ConnectionString { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();
        var opts = new DbContextOptionsBuilder<BeaconDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;
        await using var db = new BeaconDbContext(opts);
        await db.Database.EnsureCreatedAsync();
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}

[Trait("Category", "Integration")]
public class SqlServerDatabaseTests : DatabaseProviderTests, IClassFixture<SqlServerContainerFixture>
{
    private readonly SqlServerContainerFixture _fixture;
    public SqlServerDatabaseTests(SqlServerContainerFixture fixture) => _fixture = fixture;

    protected override BeaconDbContext CreateDbContext()
    {
        var opts = new DbContextOptionsBuilder<BeaconDbContext>()
            .UseSqlServer(_fixture.ConnectionString)
            .Options;
        return new BeaconDbContext(opts);
    }
}

// ─── PostgreSQL ────────────────────────────────────────────────────────────────

public class PostgreSqlContainerFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16").Build();
    public string ConnectionString { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();
        var opts = new DbContextOptionsBuilder<BeaconDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        await using var db = new BeaconDbContext(opts);
        await db.Database.EnsureCreatedAsync();
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}

[Trait("Category", "Integration")]
public class PostgreSqlDatabaseTests : DatabaseProviderTests, IClassFixture<PostgreSqlContainerFixture>
{
    private readonly PostgreSqlContainerFixture _fixture;
    public PostgreSqlDatabaseTests(PostgreSqlContainerFixture fixture) => _fixture = fixture;

    protected override BeaconDbContext CreateDbContext()
    {
        var opts = new DbContextOptionsBuilder<BeaconDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;
        return new BeaconDbContext(opts);
    }
}

// ─── MySQL ─────────────────────────────────────────────────────────────────────

public class MySqlContainerFixture : IAsyncLifetime
{
    private readonly MySqlContainer _container = new MySqlBuilder("mysql:8").Build();
    public string? ConnectionString { get; private set; }
    public string? SkipReason { get; private set; }

    public async ValueTask InitializeAsync()
    {
        try
        {
            await _container.StartAsync();
            ConnectionString = _container.GetConnectionString();
            var opts = new DbContextOptionsBuilder<BeaconDbContext>()
                .UseMySQL(ConnectionString)
                .Options;
            await using var db = new BeaconDbContext(opts);
            await db.Database.EnsureCreatedAsync();
        }
        catch (MissingMethodException ex)
        {
            SkipReason = $"MySQL provider incompatible with the current EF Core version: {ex.Message}";
        }
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}

[Trait("Category", "Integration")]
public class MySqlDatabaseTests : DatabaseProviderTests, IClassFixture<MySqlContainerFixture>
{
    private readonly MySqlContainerFixture _fixture;
    public MySqlDatabaseTests(MySqlContainerFixture fixture) => _fixture = fixture;

    protected override BeaconDbContext CreateDbContext()
    {
        if (_fixture.SkipReason is not null)
            Assert.Skip(_fixture.SkipReason);

        var opts = new DbContextOptionsBuilder<BeaconDbContext>()
            .UseMySQL(_fixture.ConnectionString!)
            .Options;
        return new BeaconDbContext(opts);
    }
}
