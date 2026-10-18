using System.Text.Encodings.Web;
using Beacon.Api;
using Beacon.Core.Models;
using Beacon.Core.Security;
using Beacon.Core.Services;
using Beacon.Localization;
using Beacon.Security;
using Beacon.Storage;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Beacon.Tests;

public class SecurityAuditTests : IDisposable
{
    private static readonly byte[] SigningKey = Convert.FromBase64String(
        "dGVzdC1zaWduaW5nLWtleS1mb3ItdGVzdGluZy1vbmx5LWtleQ==");

    private readonly SqliteConnection _connection;
    private readonly BeaconDbContext _db;

    public SecurityAuditTests()
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

    [Theory]
    [InlineData("x\"><script>alert(1)</script>", "en")]
    [InlineData(null, "en")]
    [InlineData("NL", "nl")]
    public void Normalize_AllowlistsLanguage(string? input, string expected) =>
        Assert.Equal(expected, FormLocalization.Normalize(input));

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    public async Task DoubleOptIn_RequiredByBucketOrForm(bool bucketFlag, bool formFlag, bool expected)
    {
        var repo = new BucketOptionsRepository(_db);
        await repo.SaveAsync(new BucketOptions { Bucket = "news", DoubleOptIn = bucketFlag });
        var config = new SystemConfig { EnableDoubleOptIn = true, EmailNotifications = true, EmailProvider = "smtp" };

        Assert.Equal(expected, await DoubleOptIn.IsActiveAsync(new InstanceOptions(), config, repo, "news", formFlag));
    }

    [Fact]
    public async Task DoubleOptIn_InactiveWhenInstanceDisablesEmail()
    {
        var config = new SystemConfig { EnableDoubleOptIn = true, EmailNotifications = true, EmailProvider = "smtp" };

        Assert.False(await DoubleOptIn.IsActiveAsync(
            new InstanceOptions { DisableEmailNotifications = true }, config, new BucketOptionsRepository(_db), "news", true));
    }

    [Fact]
    public async Task Jwt_DeletedUser_IsRejected()
    {
        var token = JwtAuthHandler.CreateToken(SigningKey, "ghost", DateTimeOffset.UtcNow.AddHours(1), "admin");

        Assert.False((await AuthenticateAsync(token)).Succeeded);
    }

    [Fact]
    public async Task Jwt_AdminKeySession_NeedsNoUserRecord()
    {
        var token = JwtAuthHandler.CreateToken(SigningKey, "admin", DateTimeOffset.UtcNow.AddHours(1), "admin", adminKey: true);

        var result = await AuthenticateAsync(token);
        Assert.True(result.Succeeded);
        Assert.True(result.Principal!.HasClaim(JwtAuthHandler.AdminKeyClaim, "true"));
    }

    [Fact]
    public async Task Jwt_ExistingUser_GetsRoleFromDatabase()
    {
        var (hash, salt) = PasswordHasher.HashPassword("seeded-password-12!");
        await new UserRepository(_db).CreateAsync("bob", hash, salt, "user", ApiKeyGenerator.Generate().apiKeyHash);
        var token = JwtAuthHandler.CreateToken(SigningKey, "bob", DateTimeOffset.UtcNow.AddHours(1), "admin");

        var result = await AuthenticateAsync(token);
        Assert.True(result.Succeeded);
        Assert.True(result.Principal!.IsInRole("user"));
    }

    [Theory]
    [InlineData("https://user:pw@crm.example.com/hook?token=abc", "https://crm.example.com/hook?********")]
    [InlineData("https://crm.example.com/a@b", "https://crm.example.com/a@b")]
    [InlineData("not a url", "********")]
    public void MaskUrl_DropsCredentials(string url, string expected) =>
        Assert.Equal(expected, AdminEndpoints.MaskUrl(url));

    [Theory]
    [InlineData("=HYPERLINK(\"x\")", "\"'=HYPERLINK(\"\"x\"\")\"")]
    [InlineData("+1", "'+1")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("plain", "plain")]
    public void CsvEscape_NeutralisesFormulas(string value, string expected) =>
        Assert.Equal(expected, AdminEndpoints.CsvEscape(value));

    [Theory]
    [InlineData("admin")]
    [InlineData("Admin")]
    [InlineData("APIKEYUSER")]
    public void ValidateUsername_RejectsReservedNames(string username) =>
        Assert.False(UserEndpoints.ValidateUsername(username, out _));

    [Fact]
    public void ValidateUsername_AcceptsOtherAdminNames() =>
        Assert.True(UserEndpoints.ValidateUsername("admin.jane", out _));

    [Fact]
    public async Task NamedApiKey_PrincipalNeverMatchesAUsername()
    {
        var (hash, salt) = PasswordHasher.HashPassword("seeded-password-12!");
        await new UserRepository(_db).CreateAsync("jane", hash, salt, "admin", ApiKeyGenerator.Generate().apiKeyHash);
        var (key, keyHash) = ApiKeyGenerator.Generate();
        await new ApiKeyRepository(_db).CreateAsync("jane", keyHash, ["consent:read"], true, null, null);

        using var provider = new ServiceCollection()
            .AddDbContext<BeaconDbContext>(o => o.UseSqlite(_connection))
            .AddScoped<UserRepository>()
            .AddScoped<ApiKeyRepository>()
            .BuildServiceProvider();

        var handler = new ApiKeyAuthHandler(new StaticOptionsMonitor<ApiKeyAuthOptions>(new ApiKeyAuthOptions()), NullLoggerFactory.Instance, UrlEncoder.Default,
            provider.GetRequiredService<IServiceScopeFactory>());
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = key;
        await handler.InitializeAsync(new AuthenticationScheme(ApiKeyAuthExtensions.SchemeName, null, typeof(ApiKeyAuthHandler)), context);

        var result = await handler.AuthenticateAsync();
        Assert.True(result.Succeeded);
        Assert.Equal("key:jane", result.Principal!.Identity!.Name);
    }

    private async Task<AuthenticateResult> AuthenticateAsync(string token)
    {
        var options = new JwtAuthOptions { SigningKey = SigningKey, UserAuthentication = "both" };
        var handler = new JwtAuthHandler(new StaticOptionsMonitor<JwtAuthOptions>(options), NullLoggerFactory.Instance, UrlEncoder.Default, new UserRepository(_db));
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = $"{JwtAuthHandler.CookieName}={token}";
        await handler.InitializeAsync(new AuthenticationScheme(JwtAuthExtensions.SchemeName, null, typeof(JwtAuthHandler)), context);
        return await handler.AuthenticateAsync();
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
