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
using Beacon.Tests.Fakes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
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
    public async Task Jwt_AuthTime_SurvivesAsClaim()
    {
        var signIn = DateTimeOffset.UtcNow.AddHours(-3);
        var token = JwtAuthHandler.CreateToken(SigningKey, "admin", DateTimeOffset.UtcNow.AddHours(1), "admin", adminKey: true, authTime: signIn);

        var result = await AuthenticateAsync(token);
        Assert.Equal(signIn.ToUnixTimeSeconds().ToString(), result.Principal!.FindFirst(JwtAuthHandler.AuthTimeClaim)!.Value);
    }

    [Theory]
    [InlineData("admin", null, true)]
    [InlineData("user", null, false)]
    [InlineData("apikey", "consent:read", false)]
    [InlineData("apikey", "consent:export", true)]
    [InlineData("apikey", "_all", true)]
    public void ConsentExport_ExcludesUserRole(string role, string? permission, bool expected)
    {
        List<System.Security.Claims.Claim> claims = [new(System.Security.Claims.ClaimTypes.Role, role)];
        if (permission != null) claims.Add(new("beacon:permission", permission));

        Assert.Equal(expected, ApiKeyEndpoints.CanExportConsent(new(new System.Security.Claims.ClaimsIdentity(claims, "test"))));
    }

    [Fact]
    public void RefreshedExpiry_CapsAtMaxSessionLifetime()
    {
        var signIn = DateTimeOffset.UtcNow;
        var slide = TimeSpan.FromHours(1);
        var cap = signIn + JwtAuthHandler.MaxSessionLifetime;

        Assert.Equal(signIn.AddHours(2), JwtAuthHandler.RefreshedExpiry(signIn, signIn.AddHours(1), slide));
        Assert.Equal(cap, JwtAuthHandler.RefreshedExpiry(signIn, cap.AddMinutes(-30), slide));
        Assert.Null(JwtAuthHandler.RefreshedExpiry(signIn, cap, slide));
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

    private async Task<AuthenticateResult> ApiKeyAuthAsync(Action<HttpRequest> setHeaders, string adminKey = "")
    {
        using var provider = new ServiceCollection()
            .AddDbContext<BeaconDbContext>(o => o.UseSqlite(_connection))
            .AddScoped<UserRepository>()
            .AddScoped<ApiKeyRepository>()
            .BuildServiceProvider();
        var handler = new ApiKeyAuthHandler(new StaticOptionsMonitor<ApiKeyAuthOptions>(new ApiKeyAuthOptions { AdminApiKey = adminKey }), NullLoggerFactory.Instance, UrlEncoder.Default,
            provider.GetRequiredService<IServiceScopeFactory>());
        var context = new DefaultHttpContext();
        setHeaders(context.Request);
        await handler.InitializeAsync(new AuthenticationScheme(ApiKeyAuthExtensions.SchemeName, null, typeof(ApiKeyAuthHandler)), context);
        return await handler.AuthenticateAsync();
    }

    [Fact]
    public async Task ApiKey_AsBearer_Authenticates()
    {
        var (key, keyHash) = ApiKeyGenerator.Generate();
        await new ApiKeyRepository(_db).CreateAsync("integration", keyHash, ["consent:read"], true, null, null);

        var result = await ApiKeyAuthAsync(r => r.Headers.Authorization = $"Bearer {key}");

        Assert.True(result.Succeeded);
        Assert.Equal("key:integration", result.Principal!.Identity!.Name);
    }

    [Fact]
    public async Task AdminKey_AsBearer_Authenticates()
    {
        const string adminKey = "q+K3/zR8mPpY7wT2vL5nH1xC4bF6dJ9sA0eG8uI2oKw=";

        Assert.True((await ApiKeyAuthAsync(r => r.Headers.Authorization = $"Bearer {adminKey}", adminKey)).Succeeded);
        Assert.False((await ApiKeyAuthAsync(r => r.Headers.Authorization = "Bearer not-the-key", adminKey)).Succeeded);
    }

    [Fact]
    public async Task SessionJwt_AsBearer_IsNotAnApiKey()
    {
        var token = JwtAuthHandler.CreateToken(SigningKey, "admin", DateTimeOffset.UtcNow.AddHours(1), "admin", adminKey: true);

        Assert.True((await ApiKeyAuthAsync(r => r.Headers.Authorization = $"Bearer {token}")).None);
    }

    [Fact]
    public async Task ApiKeyChallenge_SendsWwwAuthenticate()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var handler = new ApiKeyAuthHandler(new StaticOptionsMonitor<ApiKeyAuthOptions>(new ApiKeyAuthOptions()), NullLoggerFactory.Instance, UrlEncoder.Default,
            provider.GetRequiredService<IServiceScopeFactory>());
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        await handler.InitializeAsync(new AuthenticationScheme(ApiKeyAuthExtensions.SchemeName, null, typeof(ApiKeyAuthHandler)), context);

        await handler.ChallengeAsync(null);

        Assert.Equal(401, context.Response.StatusCode);
        Assert.Equal("Bearer", context.Response.Headers.WWWAuthenticate.ToString());
    }

    [Theory]
    [InlineData("invalid", 404)]
    [InlineData("expired", 410)]
    [InlineData("archived", 410)]
    [InlineData("already_processed", 409)]
    [InlineData("confirmed", 200)]
    [InlineData("unsubscribed", 200)]
    public void LinkStatusPages_UseMatchingStatusCodes(string status, int expected) =>
        Assert.Equal(expected, ConsentEndpoints.StatusCodeFor(status));

    [Fact]
    public async Task SeeOther_Returns303WithLocation()
    {
        var context = new DefaultHttpContext();
        await SubmissionEndpoints.SeeOther("https://example.com/thanks").ExecuteAsync(context);

        Assert.Equal(303, context.Response.StatusCode);
        Assert.Equal("https://example.com/thanks", context.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task ServerError_Returns500WithTraceId()
    {
        var context = new DefaultHttpContext { TraceIdentifier = "trace-1" };
        context.Response.Body = new MemoryStream();
        await ApiError.ServerError().ExecuteAsync(context);

        context.Response.Body.Position = 0;
        using var body = await System.Text.Json.JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal(500, context.Response.StatusCode);
        Assert.StartsWith("application/json", context.Response.ContentType);
        Assert.False(string.IsNullOrEmpty(body.RootElement.GetProperty("traceId").GetString()));
        Assert.False(string.IsNullOrEmpty(body.RootElement.GetProperty("error").GetString()));
    }

    [Fact]
    public void SubmissionBodies_OmitRedirectWhenUnset()
    {
        var web = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);

        Assert.Equal("{\"error\":\"x\"}", System.Text.Json.JsonSerializer.Serialize(new SubmissionError("x"), web));
        Assert.Equal("{\"message\":\"ok\",\"redirect\":\"https://a.example\"}", System.Text.Json.JsonSerializer.Serialize(new SubmissionSuccess("ok", "https://a.example"), web));
    }

    [Fact]
    public void SchemeSelector_RoutesBySessionTokenShape()
    {
        var jwt = JwtAuthHandler.CreateToken(SigningKey, "admin", DateTimeOffset.UtcNow.AddHours(1), "admin");
        var (key, _) = ApiKeyGenerator.Generate();
        string Select(Action<HttpRequest> set) { var c = new DefaultHttpContext(); set(c.Request); return JwtAuthExtensions.SelectScheme(c); }

        Assert.Equal(JwtAuthExtensions.SchemeName, Select(r => r.Headers.Authorization = $"Bearer {jwt}"));
        Assert.Equal(ApiKeyAuthExtensions.SchemeName, Select(r => r.Headers.Authorization = $"Bearer {key}"));
        Assert.Equal(JwtAuthExtensions.SchemeName, Select(r => r.Headers.Cookie = $"{JwtAuthHandler.CookieName}={jwt}"));
        Assert.Equal(ApiKeyAuthExtensions.SchemeName, Select(r => r.Headers["X-Api-Key"] = key));
    }

    [Fact]
    public void ExportWriter_FlattensGroupsAndQuotesYamlStrings()
    {
        List<ExportWriter.Row> rows =
        [
            new() { { "email", "a@x.com" }, { "name", "true" }, { "consent", new ExportWriter.Row { { "Newsletter", true } } } },
            new() { { "email", "b@x.com" }, { "name", null }, { "consent", new ExportWriter.Row { { "Alerts", false } } } }
        ];

        var csv = ExportWriter.ToCsv(rows);
        Assert.StartsWith("email,name,consent.Newsletter,consent.Alerts\r\n", csv);
        Assert.Contains("b@x.com,,,false\r\n", csv);

        var yaml = ExportWriter.ToYaml("records", rows);
        Assert.Contains("name: \"true\"", yaml);
        Assert.Contains("name: null", yaml);
        Assert.Equal("records: []\n", ExportWriter.ToYaml("records", []));
    }

    [Fact]
    public void FullCopyRow_IncludesConsentProofAndCustomFields()
    {
        var record = new ConsentRecord
        {
            Bucket = "b", EmailHash = "h", Permission = "news", Status = ConsentStatus.PendingConfirmation,
            Source = ConsentSource.Url, ConsentText = "I agree", ChangedAt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc)
        };
        var contact = new IdentityInfo { EmailHash = "h", Email = "a@x.com", Name = "A" };

        var csv = ExportWriter.ToCsv([AdminEndpoints.FullCopyRow(record, contact, new Dictionary<string, string> { ["city"] = "Utrecht" }, ["city", "zip"])]);

        Assert.StartsWith("beaconId,email,name,bucket,permission,status,source,consentText,changedAt,customFields.city,customFields.zip\r\n", csv);
        Assert.Contains("h,a@x.com,A,b,news,pendingConfirmation,url,I agree,2026-01-02T03:04:05Z,Utrecht,\r\n", csv);
    }

    [Fact]
    public void ExportWriter_TimestampIsUtcIso8601() =>
        Assert.Equal("2026-10-18T20:01:41Z", ExportWriter.Timestamp(new DateTime(2026, 10, 18, 20, 1, 41, 123, DateTimeKind.Unspecified)));

    [Fact]
    public async Task Jwt_RevokedSession_IsRejected()
    {
        var token = JwtAuthHandler.CreateToken(SigningKey, "admin", DateTimeOffset.UtcNow.AddHours(1), "admin", adminKey: true);
        Assert.True((await AuthenticateAsync(token)).Succeeded);

        await JwtAuthHandler.RevokeAsync(new TokenUsageRepository(_db), SigningKey, token);
        await JwtAuthHandler.RevokeAsync(new TokenUsageRepository(_db), SigningKey, token);

        Assert.False((await AuthenticateAsync(token)).Succeeded);
    }

    [Theory]
    [InlineData(false, "both", true, true)]
    [InlineData(true, "both", false, true)]
    [InlineData(true, "user", false, true)]
    [InlineData(true, "api", true, true)]
    public async Task Jwt_KeyLoginSessions_EndWhenUserSignInRequired(bool required, string userAuthentication, bool keySessionsPass, bool passwordSessionsPass)
    {
        _db.Users.Add(new User { Username = "ops", PasswordHash = "x", Salt = "x", ApiKeyHash = "x", Role = "admin" });
        await _db.SaveChangesAsync();
        var config = new SystemConfig { RequireUserSignIn = required };
        var expires = DateTimeOffset.UtcNow.AddHours(1);

        Assert.Equal(keySessionsPass, (await AuthenticateAsync(JwtAuthHandler.CreateToken(SigningKey, "admin", expires, "admin", adminKey: true), config, userAuthentication)).Succeeded);
        Assert.Equal(keySessionsPass, (await AuthenticateAsync(JwtAuthHandler.CreateToken(SigningKey, "ops", expires, "admin", keyLogin: true), config, userAuthentication)).Succeeded);
        Assert.Equal(passwordSessionsPass, (await AuthenticateAsync(JwtAuthHandler.CreateToken(SigningKey, "ops", expires, "admin"), config, userAuthentication)).Succeeded);
    }

    [Theory]
    [InlineData(false, "both", null, 200)]
    [InlineData(true, "both", null, 403)]
    [InlineData(true, "user", null, 403)]
    [InlineData(true, "", null, 200)]
    [InlineData(true, "both", "ops", 401)]
    [InlineData(false, "both", "ops", 200)]
    public async Task Login_RefusesApiKeys_WhenUserSignInRequired(bool required, string userAuthentication, string? username, int expected)
    {
        var result = await AuthEndpoints.Login(
            new AuthEndpoints.LoginRequest(ApiKey: "admin-key", Username: username, Password: username is null ? null : "wrong"),
            new DefaultHttpContext(),
            new FakeWebHostEnvironment(),
            new StaticOptionsMonitor<ApiKeyAuthOptions>(new ApiKeyAuthOptions { AdminApiKey = "admin-key", UserAuthentication = userAuthentication }),
            new StaticOptionsMonitor<JwtAuthOptions>(new JwtAuthOptions { SigningKey = SigningKey }),
            new LoginLockout(),
            new StubSystemConfigurationService(new SystemConfig { RequireUserSignIn = required }),
            new UserRepository(_db));

        Assert.Equal(expected, ((IStatusCodeHttpResult)result).StatusCode ?? 200);
    }

    [Theory]
    [InlineData("both", "admin", true, null)]
    [InlineData("user", "admin", true, null)]
    [InlineData("", "admin", true, "Password sign-in is off. Set USER_AUTHENTICATION to user or both first.")]
    [InlineData("both", "user", true, "Add an enabled admin user first, or no one can sign in to the admin panel.")]
    [InlineData("both", "admin", false, "Add an enabled admin user first, or no one can sign in to the admin panel.")]
    public async Task RequireUserSignIn_NeedsAnEnabledAdminUser(string userAuthentication, string role, bool enabled, string? problem)
    {
        _db.Users.Add(new User { Username = "ops", PasswordHash = "x", Salt = "x", ApiKeyHash = "x", Role = role, IsEnabled = enabled });
        await _db.SaveChangesAsync();

        Assert.Equal(problem, await AdminEndpoints.RequireUserSignInProblemAsync(_db, userAuthentication));
    }

    private sealed class FakeWebHostEnvironment : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = "";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "Beacon";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = "";
        public string EnvironmentName { get; set; } = "Production";
    }

    private async Task<AuthenticateResult> AuthenticateAsync(string token, SystemConfig? config = null, string userAuthentication = "both")
    {
        var options = new JwtAuthOptions { SigningKey = SigningKey, UserAuthentication = userAuthentication };
        var handler = new JwtAuthHandler(new StaticOptionsMonitor<JwtAuthOptions>(options), NullLoggerFactory.Instance, UrlEncoder.Default, new UserRepository(_db), new TokenUsageRepository(_db), new StubSystemConfigurationService(config));
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = $"{JwtAuthHandler.CookieName}={token}";
        await handler.InitializeAsync(new AuthenticationScheme(JwtAuthExtensions.SchemeName, null, typeof(JwtAuthHandler)), context);
        return await handler.AuthenticateAsync();
    }

    [Fact]
    public void AuthCookieName_IsPerInstance()
    {
        var a = JwtAuthHandler.InstanceCookieName(new byte[32], secure: true);
        var b = JwtAuthHandler.InstanceCookieName(Enumerable.Repeat((byte)1, 32).ToArray(), secure: true);

        Assert.NotEqual(a, b);
        Assert.Equal(a, JwtAuthHandler.InstanceCookieName(new byte[32], secure: true));
        Assert.StartsWith("__Host-beacon_auth_", a);
        Assert.StartsWith("beacon_auth_", JwtAuthHandler.InstanceCookieName(new byte[32], secure: false));
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
