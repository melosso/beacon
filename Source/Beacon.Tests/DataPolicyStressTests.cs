using System.Diagnostics;
using Beacon.Core.Models;
using Beacon.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Beacon.Tests;

[Trait("Category", "Stress")]
public sealed class DataPolicyStressTests : IDisposable
{
    private static readonly int Rows = int.TryParse(Environment.GetEnvironmentVariable("BEACON_STRESS_ROWS"), out var n) ? n : 20_000;
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(60);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly SqliteConnection _connection;
    private readonly BeaconDbContext _db;

    public DataPolicyStressTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _db = new BeaconDbContext(new DbContextOptionsBuilder<BeaconDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task RetentionPurge_Scales()
    {
        await SeedAsync(ConsentStatus.OptedOut);

        var (affected, elapsed) = await MeasureAsync(() => new ConsentRepository(_db).AnonymiseOptedOutAsync(DateTime.UtcNow, Ct));

        Assert.Equal(Rows, affected);
        Assert.False(await _db.ConsentRecords.AnyAsync(r => r.EncryptedEmail != null || r.EncryptedName != null || r.CustomFields != null, Ct));
        Assert.False(await _db.ConsentAuditEntries.AnyAsync(e => e.CustomFields != null, Ct));
        Assert.True(elapsed < Budget, $"{Rows} rows took {elapsed}");
    }

    [Fact]
    public async Task PendingConfirmationPurge_Scales()
    {
        await SeedAsync(ConsentStatus.PendingConfirmation);

        var (affected, elapsed) = await MeasureAsync(() => new ConsentRepository(_db).PurgePendingConfirmationAsync(DateTime.UtcNow, Ct));

        Assert.Equal(Rows, affected);
        Assert.Equal(Rows, await _db.ConsentRecords.CountAsync(r => r.Status == ConsentStatus.OptedOut && r.EncryptedEmail == null, Ct));
        Assert.True(elapsed < Budget, $"{Rows} rows took {elapsed}");
    }

    private async Task<(int Affected, TimeSpan Elapsed)> MeasureAsync(Func<Task<int>> purge)
    {
        _db.ChangeTracker.Clear();
        var sw = Stopwatch.StartNew();
        var affected = await purge();
        sw.Stop();
        _db.ChangeTracker.Clear();
        TestContext.Current.TestOutputHelper?.WriteLine($"{Rows} rows: {sw.Elapsed.TotalMilliseconds:F0} ms ({Rows / sw.Elapsed.TotalSeconds:F0} rows/s)");
        return (affected, sw.Elapsed);
    }

    private async Task SeedAsync(ConsentStatus status)
    {
        var old = DateTime.UtcNow.AddDays(-10);
        for (var i = 0; i < Rows; i++)
        {
            var hash = $"{i:x64}";
            _db.ConsentRecords.Add(new ConsentRecord
            {
                Id = Guid.NewGuid(),
                Bucket = "stress",
                EmailHash = hash,
                EncryptedEmail = "enc-email",
                EncryptedName = "enc-name",
                Permission = "newsletter",
                Status = status,
                Source = ConsentSource.Api,
                ChangedAt = old,
                CustomFields = """{"city":"Utrecht"}""",
                ConsentText = "I agree"
            });
            _db.ConsentAuditEntries.Add(new ConsentAuditEntry
            {
                Id = Guid.NewGuid(),
                Bucket = "stress",
                EmailHash = hash,
                Permission = "newsletter",
                NewStatus = status,
                Source = ConsentSource.Api,
                ChangedAt = old,
                CustomFields = """{"city":"Utrecht"}"""
            });
        }
        await _db.SaveChangesAsync(Ct);
    }
}
