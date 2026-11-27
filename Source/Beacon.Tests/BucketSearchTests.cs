using System.Text.Json;
using Beacon.Api;
using Beacon.Core.Models;
using Beacon.Core.Security;
using Beacon.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Beacon.Tests;

public sealed class BucketSearchTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly BeaconDbContext _db;
    private readonly Encryptor _encryptor = new("MDEyMzQ1Njc4OTAxMjM0NTY3ODkwMTIzNDU2Nzg5MDE=");

    public BucketSearchTests()
    {
        _connection.Open();
        _db = new BeaconDbContext(new DbContextOptionsBuilder<BeaconDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Theory]
    [InlineData("name", "bob", "Bob Ray")]
    [InlineData("name", "jane*", "Jane Doe")]
    [InlineData("email", "bob@", "Bob Ray")]
    public async Task Search_MatchesAcrossPages(string searchType, string search, string expectedName)
    {
        var repository = new ConsentRepository(_db);
        await repository.UpsertAsync(Record("hash-jane", "jane@example.com", "Jane Doe", DateTime.UtcNow));
        await repository.UpsertAsync(Record("hash-bob", "bob@example.com", "Bob Ray", DateTime.UtcNow.AddDays(-1)));

        var result = JsonSerializer.SerializeToElement(await AdminEndpoints.GetBucketRecordsSortedByContactAsync(
            "verify", 1, 1, "lastchanged", "desc", search, searchType, repository, _encryptor));

        Assert.Equal(1, result.GetProperty("total").GetInt32());
        Assert.Equal(expectedName, result.GetProperty("records")[0].GetProperty("Name").GetString());
    }

    private ConsentRecord Record(string hash, string email, string name, DateTime changedAt) => new()
    {
        Id = Guid.NewGuid(),
        Bucket = "verify",
        EmailHash = hash,
        EncryptedEmail = _encryptor.Encrypt(email),
        EncryptedName = _encryptor.Encrypt(name),
        Permission = "newsletter",
        Status = ConsentStatus.OptedIn,
        Source = ConsentSource.Admin,
        ChangedAt = changedAt
    };
}
