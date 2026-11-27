using Beacon.Api;
using Beacon.Core.Models;
using Beacon.Core.Security;
using Beacon.Security;
using Beacon.Storage;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Xunit;

namespace Beacon.Tests;

public sealed class OidcTests : IDisposable
{
    private static readonly OpenIdConnectConfiguration Document = new() { AuthorizationEndpoint = "https://idp.example.test/authorize" };
    private const string Redirect = "https://beacon.example.test/api/admin/auth/oidc/idp/callback";
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly BeaconDbContext _db;
    private readonly Encryptor _encryptor = new("MDEyMzQ1Njc4OTAxMjM0NTY3ODkwMTIzNDU2Nzg5MDE=");
    private readonly OidcProvider _provider = new() { Id = 1, Slug = "idp", Name = "IdP", Authority = "https://idp.example.test", ClientId = "beacon", IsEnabled = true };

    public OidcTests()
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

    [Fact]
    public void Begin_SendsPkceAndSpendsStateOnce()
    {
        var start = OidcFlow.Begin(Document, _provider, Redirect, DateTime.UtcNow);
        var query = QueryHelpers.ParseQuery(new Uri(start.AuthorizeUrl).Query);

        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Equal(start.State, query["state"]);
        Assert.Equal(Redirect, query["redirect_uri"]);
        Assert.NotNull(OidcFlow.Claim(start.State));
        Assert.Null(OidcFlow.Claim(start.State));
    }

    [Fact]
    public void Claim_ExpiredFlow_ReturnsNull()
    {
        var start = OidcFlow.Begin(Document, _provider, Redirect, DateTime.UtcNow - OidcFlow.FlowLifetime - TimeSpan.FromMinutes(1));

        Assert.Null(OidcFlow.Claim(start.State));
    }

    [Fact]
    public async Task Resolve_BoundSubjectWins()
    {
        var bound = await AddUser("alice", provider: "idp", externalId: "sub-1");
        await AddUser("alice@example.com");

        var (account, _) = await OidcEndpoints.ResolveAccountAsync(_db, _provider, new OidcIdentity("sub-1", "x", "alice@example.com", true));

        Assert.Equal(bound.Id, account!.Id);
    }

    [Fact]
    public async Task Resolve_VerifiedEmailBindsLocalAccount()
    {
        var local = await AddUser("alice@example.com");

        var (account, _) = await OidcEndpoints.ResolveAccountAsync(_db, _provider, new OidcIdentity("sub-1", "alice", "Alice@Example.com", true));

        Assert.Equal(local.Id, account!.Id);
        Assert.Equal("idp", account.Provider);
        Assert.Equal("sub-1", account.ExternalId);
    }

    [Fact]
    public async Task Resolve_UnverifiedEmailOrOtherProvider_IsRefused()
    {
        await AddUser("alice@example.com");
        await AddUser("bob@example.com", provider: "other", externalId: "x");

        var (unverified, problem) = await OidcEndpoints.ResolveAccountAsync(_db, _provider, new OidcIdentity("sub-1", "alice", "alice@example.com", false));
        var (other, _) = await OidcEndpoints.ResolveAccountAsync(_db, _provider, new OidcIdentity("sub-2", "bob", "bob@example.com", true));

        Assert.Null(unverified);
        Assert.Equal(OidcFlow.NoAccount, problem);
        Assert.Null(other);
    }

    [Fact]
    public async Task Resolve_CreateAccounts_ProvisionsUniqueUserWithRole()
    {
        await AddUser("alice");
        _provider.CreateAccounts = true;
        _provider.CreatedRole = "user";

        var (account, _) = await OidcEndpoints.ResolveAccountAsync(_db, _provider, new OidcIdentity("sub-9", "alice", "", false));
        await _db.SaveChangesAsync();

        Assert.Equal("alice-2", account!.Username);
        Assert.Equal("user", account.Role);
        Assert.Equal("sub-9", account.ExternalId);
        Assert.Null(account.Email);
    }

    [Fact]
    public async Task Resolve_DisabledAccount_IsInactive()
    {
        await AddUser("carol", provider: "idp", externalId: "sub-3", enabled: false);

        var (account, problem) = await OidcEndpoints.ResolveAccountAsync(_db, _provider, new OidcIdentity("sub-3", "carol", "", false));

        Assert.Null(account);
        Assert.Equal(OidcFlow.Inactive, problem);
    }

    [Theory]
    [InlineData("Bad Key", "https://idp.example.test", null, null, "key")]
    [InlineData("idp", "http://idp.example.test", null, null, "loopback")]
    [InlineData("idp", "https://idp.example.test", "profile email", null, "openid")]
    [InlineData("idp", "https://idp.example.test", null, "owner", "Role")]
    public void Apply_RejectsInvalidInput(string slug, string authority, string? scopes, string? role, string expected)
    {
        var problem = OidcEndpoints.Apply(new OidcEndpoints.ProviderRequest(slug, "IdP", authority, "beacon", null, scopes, null, null, null, null, role),
            new OidcProvider(), _encryptor, isNew: true);

        Assert.Contains(expected, problem);
    }

    [Fact]
    public void Apply_EncryptsSecretAndEmptyClearsIt()
    {
        var provider = new OidcProvider();
        Assert.Null(OidcEndpoints.Apply(new OidcEndpoints.ProviderRequest("idp", "IdP", "http://127.0.0.1:9091", "beacon", "s3cret", null, null, null, true, null, null),
            provider, _encryptor, isNew: true));

        Assert.NotEqual("s3cret", provider.EncryptedClientSecret);
        Assert.Equal("s3cret", _encryptor.Decrypt(provider.EncryptedClientSecret));

        OidcEndpoints.Apply(new OidcEndpoints.ProviderRequest(null, null, null, null, "", null, null, null, null, null, null), provider, _encryptor, isNew: false);
        Assert.Equal("", provider.EncryptedClientSecret);
    }

    [Theory]
    [InlineData("GET", "/api/admin/oidc/providers", "Admin")]
    [InlineData("POST", "/api/admin/oidc/providers", "Admin")]
    [InlineData("PUT", "/api/admin/oidc/providers/{id:int}", "Admin")]
    [InlineData("DELETE", "/api/admin/oidc/providers/{id:int}", "Admin")]
    [InlineData("GET", "/api/admin/auth/oidc/{slug}/callback", null)]
    public void Endpoints_HaveExpectedPolicy(string method, string route, string? policy)
    {
        var app = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder().Build();
        app.MapOidcEndpoints();

        var endpoint = ((Microsoft.AspNetCore.Routing.IEndpointRouteBuilder)app).DataSources
            .SelectMany(d => d.Endpoints)
            .OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>()
            .Single(e => e.RoutePattern.RawText == route
                && e.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()!.HttpMethods.Contains(method));

        var policies = endpoint.Metadata.GetOrderedMetadata<Microsoft.AspNetCore.Authorization.IAuthorizeData>().Select(a => a.Policy).ToArray();
        Assert.Equal(policy is null ? [] : [policy], policies);
    }

    private async Task<User> AddUser(string username, string? provider = null, string? externalId = null, bool enabled = true)
    {
        var (hash, salt) = PasswordHasher.HashPassword("Password-123!");
        var user = new User
        {
            Id = Guid.NewGuid(), Username = username, PasswordHash = hash, Salt = salt, Role = "admin",
            ApiKeyHash = ApiKeyGenerator.Generate().apiKeyHash, IsEnabled = enabled, CreatedAt = DateTime.UtcNow,
            Provider = provider, ExternalId = externalId
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }
}
