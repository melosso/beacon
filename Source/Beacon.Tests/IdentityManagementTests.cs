using Beacon.Api;
using Beacon.Core.Models;
using Beacon.Core.Security;
using Beacon.Core.Services;
using Beacon.Storage;
using Beacon.Tests.Fakes;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Beacon.Tests;

public sealed class IdentityManagementTests : IDisposable
{
    private const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string OtherHash = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly BeaconDbContext _db;
    private readonly ConsentRepository _repository;
    private readonly Encryptor _encryptor = new("MDEyMzQ1Njc4OTAxMjM0NTY3ODkwMTIzNDU2Nzg5MDE=");

    public IdentityManagementTests()
    {
        _connection.Open();
        _db = new BeaconDbContext(new DbContextOptionsBuilder<BeaconDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
        _repository = new ConsentRepository(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task DeleteIdentity_RemovesEveryBucketAndSendsOneNoticePerBucket()
    {
        await Seed(Hash, "bucket-a", "newsletter", "alerts");
        await Seed(Hash, "bucket-b", "newsletter");
        await Seed(OtherHash, "bucket-a", "newsletter");
        var queue = new WebhookDeliveryQueue();
        var webhooks = new WebhookService(new WebhookRepository(_db), _encryptor, queue);
        await webhooks.SaveWebhookConfigAsync("bucket-a", "https://example.com/a", "POST", null, null);
        await webhooks.SaveWebhookConfigAsync("bucket-b", "https://example.com/b", "POST", null, null);

        var result = await AdminEndpoints.DeleteIdentity(Hash, Admin(), _repository, new AdminNotificationService(), webhooks,
            new StubSystemConfigurationService(), _encryptor, NullLogger<Program>.Instance);

        Assert.Equal(StatusCodes.Status200OK, ((IStatusCodeHttpResult)result).StatusCode);
        Assert.Equal(0, await _db.ConsentRecords.CountAsync(r => r.EmailHash == Hash));
        Assert.Equal(1, await _db.ConsentRecords.CountAsync(r => r.EmailHash == OtherHash));
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var buckets = new List<string>();
        await foreach (var message in queue.DequeueAllAsync(cts.Token))
        {
            buckets.Add(message.Bucket);
            if (buckets.Count == 2) break;
        }
        Assert.Equal(["bucket-a", "bucket-b"], buckets.Order());
    }

    [Fact]
    public async Task DeleteIdentity_ReplacesHistoryWithErasureEntries()
    {
        await Seed(Hash, "bucket-a", "newsletter", "alerts");
        await _repository.UpsertAsync(new ConsentRecord
        {
            Id = Guid.NewGuid(), Bucket = "bucket-a", EmailHash = Hash, Permission = "newsletter",
            Status = ConsentStatus.OptedOut, Source = ConsentSource.Url, ChangedAt = DateTime.UtcNow
        });

        await AdminEndpoints.DeleteIdentity(Hash, Admin(), _repository, new AdminNotificationService(),
            new WebhookService(new WebhookRepository(_db), _encryptor, new WebhookDeliveryQueue()),
            new StubSystemConfigurationService(), _encryptor, NullLogger<Program>.Instance);

        var audit = await _db.ConsentAuditEntries.AsNoTracking().Where(e => e.EmailHash == Hash).OrderBy(e => e.Permission).ToListAsync();
        Assert.Equal(["alerts", "newsletter"], audit.Select(e => e.Permission));
        Assert.All(audit, e =>
        {
            Assert.Equal(ConsentSource.Erasure, e.Source);
            Assert.Equal(ConsentStatus.OptedOut, e.NewStatus);
            Assert.Equal("alice", e.ActorId);
            Assert.Null(e.CustomFields);
        });
        Assert.Equal(ConsentStatus.OptedOut, audit.Single(e => e.Permission == "newsletter").OldStatus);
    }

    [Fact]
    public async Task DeleteIdentity_UnknownHash_ReturnsNotFound()
    {
        var result = await AdminEndpoints.DeleteIdentity(Hash, Admin(), _repository, new AdminNotificationService(),
            new WebhookService(new WebhookRepository(_db), _encryptor, new WebhookDeliveryQueue()),
            new StubSystemConfigurationService(), _encryptor, NullLogger<Program>.Instance);

        Assert.Equal(StatusCodes.Status404NotFound, ((IStatusCodeHttpResult)result).StatusCode);
    }

    [Fact]
    public async Task DeleteIdentities_DeletesSelectedOnlyAndValidates()
    {
        await Seed(Hash, "bucket-a", "newsletter");
        await Seed(OtherHash, "bucket-b", "newsletter");
        const string Third = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";
        await Seed(Third, "bucket-a", "newsletter");
        var webhooks = new WebhookService(new WebhookRepository(_db), _encryptor, new WebhookDeliveryQueue());

        var result = await AdminEndpoints.DeleteIdentities(new AdminEndpoints.DeleteIdentitiesRequest([Hash, OtherHash, Hash]), Admin(), _repository,
            new AdminNotificationService(), webhooks, new StubSystemConfigurationService(), _encryptor, NullLogger<Program>.Instance);
        var invalid = await AdminEndpoints.DeleteIdentities(new AdminEndpoints.DeleteIdentitiesRequest(["not-a-hash"]), Admin(), _repository,
            new AdminNotificationService(), webhooks, new StubSystemConfigurationService(), _encryptor, NullLogger<Program>.Instance);

        Assert.Equal(StatusCodes.Status200OK, ((IStatusCodeHttpResult)result).StatusCode);
        Assert.Equal(StatusCodes.Status400BadRequest, ((IStatusCodeHttpResult)invalid).StatusCode);
        Assert.Equal(0, await _db.ConsentRecords.CountAsync(r => r.EmailHash == Hash || r.EmailHash == OtherHash));
        Assert.Equal(1, await _db.ConsentRecords.CountAsync(r => r.EmailHash == Third));
    }

    [Fact]
    public async Task UpdateIdentityName_SetsNameInEveryBucketOnly()
    {
        await Seed(Hash, "bucket-a", "newsletter");
        await Seed(Hash, "bucket-b", "newsletter");
        await Seed(OtherHash, "bucket-a", "newsletter");

        var result = await AdminEndpoints.UpdateIdentityName(Hash, new AdminEndpoints.IdentityNameRequest("  Jane Roe "), _repository, _encryptor);

        Assert.Equal(StatusCodes.Status200OK, ((IStatusCodeHttpResult)result).StatusCode);
        var names = await _db.ConsentRecords.AsNoTracking().Where(r => r.EmailHash == Hash).Select(r => r.EncryptedName).ToListAsync();
        Assert.All(names, n => Assert.Equal("Jane Roe", _encryptor.Decrypt(n!)));
        var other = await _db.ConsentRecords.AsNoTracking().SingleAsync(r => r.EmailHash == OtherHash);
        Assert.Equal("Old Name", _encryptor.Decrypt(other.EncryptedName!));
    }

    [Fact]
    public async Task UpdateIdentityName_RejectsLongNameAndUnknownHash()
    {
        var tooLong = await AdminEndpoints.UpdateIdentityName(Hash, new AdminEndpoints.IdentityNameRequest(new string('x', 251)), _repository, _encryptor);
        var unknown = await AdminEndpoints.UpdateIdentityName(Hash, new AdminEndpoints.IdentityNameRequest("Jane"), _repository, _encryptor);

        Assert.Equal(StatusCodes.Status400BadRequest, ((IStatusCodeHttpResult)tooLong).StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, ((IStatusCodeHttpResult)unknown).StatusCode);
    }

    [Theory]
    [InlineData("DELETE", "/api/admin/identities/{emailHash}")]
    [InlineData("PUT", "/api/admin/identities/{emailHash}/name")]
    [InlineData("POST", "/api/admin/identities/delete")]
    public void IdentityWriteEndpoints_RequireConsentWrite(string method, string route)
    {
        var app = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder().Build();
        app.MapAdminEndpoints();

        var endpoint = ((Microsoft.AspNetCore.Routing.IEndpointRouteBuilder)app).DataSources
            .SelectMany(d => d.Endpoints)
            .OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>()
            .Single(e => e.RoutePattern.RawText == route
                && e.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()!.HttpMethods.Contains(method));

        var policies = endpoint.Metadata.GetOrderedMetadata<Microsoft.AspNetCore.Authorization.IAuthorizeData>().Select(a => a.Policy);
        Assert.Equal(["ConsentWrite"], policies);
    }

    private static DefaultHttpContext Admin() => new()
    {
        User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity([new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "alice")], "test"))
    };

    private async Task Seed(string hash, string bucket, params string[] permissions)
    {
        foreach (var permission in permissions)
        {
            await _repository.UpsertAsync(new ConsentRecord
            {
                Id = Guid.NewGuid(),
                Bucket = bucket,
                EmailHash = hash,
                EncryptedEmail = _encryptor.Encrypt($"{hash[..4]}@example.com"),
                EncryptedName = _encryptor.Encrypt("Old Name"),
                Permission = permission,
                Status = ConsentStatus.OptedIn,
                Source = ConsentSource.Admin,
                ChangedAt = DateTime.UtcNow
            });
        }
    }
}
