using System.Net;
using System.Text.RegularExpressions;
using Beacon.Api;
using Beacon.Core.Models;
using Beacon.Core.Security;
using Beacon.Core.Services;
using Beacon.Storage;
using Beacon.Tests.Fakes;
using Beacon.Tokens;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Beacon.Tests;

public sealed partial class PreferenceLinkAttributionTests : IAsyncLifetime
{
    private const string Bucket = "news";
    private const string Permission = "newsletter";

    private static readonly TokenOptions TokenOptions = new() { SigningKey = "dGVzdC1zaWduaW5nLWtleS1mb3ItdGVzdGluZy1vbmx5LWtleQ==" };

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private WebApplication _app = null!;
    private Uri _baseUri = null!;

    public async ValueTask InitializeAsync()
    {
        _connection.Open();
        var dbOptions = new DbContextOptionsBuilder<BeaconDbContext>().UseSqlite(_connection).Options;
        using (var db = new BeaconDbContext(dbOptions))
            db.Database.EnsureCreated();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services
            .AddScoped(_ => new BeaconDbContext(dbOptions))
            .AddScoped<IConsentRepository, ConsentRepository>()
            .AddScoped<TokenUsageRepository>()
            .AddScoped<BucketRepository>()
            .AddScoped<ConsentService>()
            .AddSingleton<IBeaconCacheService, NullBeaconCacheService>()
            .AddSingleton<ISystemConfigurationService>(new StubSystemConfigurationService())
            .AddSingleton(new EmailHasher("test-pepper"))
            .AddSingleton(new Encryptor("MDEyMzQ1Njc4OTAxMjM0NTY3ODkwMTIzNDU2Nzg5MDE="))
            .AddSingleton(new TokenValidator(TokenOptions))
            .AddSingleton<IWebhookService, SubmissionFormServiceTests.InMemoryWebhookService>()
            .AddSingleton<AdminNotificationService>()
            .AddSingleton<BrandIdentityService>()
            .AddAntiforgery();

        _app = builder.Build();
        _app.MapConsentEndpoints();
        await _app.StartAsync(TestContext.Current.CancellationToken);
        _baseUri = new Uri(_app.Urls.First());
    }

    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync();
        _connection.Dispose();
    }

    [Fact]
    public async Task AssistSecret_BindsIssuerBrowser_AndPastedLinkStaysSubscriber()
    {
        var (secret, hash) = TokenGenerator.CreateAssistSecret();
        var generator = new TokenGenerator(TokenOptions);
        var adminToken = generator.Generate(Bucket, "admin-view@example.com", [Permission], issuer: "anna", assistHash: hash);
        var pastedToken = generator.Generate(Bucket, "pasted@example.com", [Permission], issuer: "anna", assistHash: hash);
        await SeedOptedInAsync("admin-view@example.com", "pasted@example.com");

        using var adminBrowser = NewBrowser();
        var handshake = await adminBrowser.GetAsync($"/u/{adminToken}?assist={secret}&utm_source=crm", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, handshake.StatusCode);
        Assert.DoesNotContain("assist", handshake.Headers.Location!.OriginalString);
        Assert.Contains("utm_source=crm", handshake.Headers.Location!.OriginalString);
        Assert.Contains(handshake.Headers.GetValues("Set-Cookie"), c => c.StartsWith("beacon_assist=", StringComparison.Ordinal));

        var adminPage = await adminBrowser.GetStringAsync(handshake.Headers.Location, TestContext.Current.CancellationToken);
        Assert.Contains("You're viewing this as an admin. Any changes you make will be logged under your anna account.", adminPage);
        await OptOutAsync(adminBrowser, adminToken, adminPage);

        // Same secret in a pasted URL: a different browser with no cookie.
        using var subscriberBrowser = NewBrowser();
        var subscriberPage = await subscriberBrowser.GetStringAsync($"/u/{pastedToken}", TestContext.Current.CancellationToken);
        Assert.DoesNotContain("You're viewing this as an admin", subscriberPage);
        await OptOutAsync(subscriberBrowser, pastedToken, subscriberPage);

        var admin = await LatestAuditAsync("admin-view@example.com");
        Assert.Equal(ConsentSource.AdminLink, admin.Source);
        Assert.Equal("anna", admin.ActorId);
        Assert.Equal("anna", admin.IssuedBy);

        var subscriber = await LatestAuditAsync("pasted@example.com");
        Assert.Equal(ConsentSource.Url, subscriber.Source);
        Assert.Null(subscriber.ActorId);
        Assert.Equal("anna", subscriber.IssuedBy);
    }

    [Fact]
    public async Task WrongAssistSecret_RedirectsWithoutBinding()
    {
        var (_, hash) = TokenGenerator.CreateAssistSecret();
        var token = new TokenGenerator(TokenOptions).Generate(Bucket, "x@example.com", [Permission], issuer: "anna", assistHash: hash);

        using var browser = NewBrowser();
        var response = await browser.GetAsync($"/u/{token}?assist={TokenGenerator.CreateAssistSecret().Secret}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out var cookies)
            && cookies.Any(c => c.StartsWith("beacon_assist=", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task CreatePreferenceLink_UsesPermissionsFromRecords()
    {
        await SeedOptedInAsync("record-only@example.com");
        using var scope = _app.Services.CreateScope();
        var services = scope.ServiceProvider;
        var hash = services.GetRequiredService<EmailHasher>().Hash("record-only@example.com");
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext
        {
            User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
                [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "anna")], "test"))
        };

        var result = await AdminEndpoints.CreatePreferenceLink(Bucket, hash, context, null,
            services.GetRequiredService<IConsentRepository>(), services.GetRequiredService<BucketRepository>(),
            new TokenGenerator(TokenOptions), services.GetRequiredService<Encryptor>());

        var json = System.Text.Json.JsonSerializer.SerializeToElement(
            Assert.IsAssignableFrom<Microsoft.AspNetCore.Http.IValueHttpResult>(result).Value);
        var payload = new TokenValidator(TokenOptions).Validate(json.GetProperty("token").GetString()!).Payload!;
        Assert.Equal([Permission], payload.Permissions);
        Assert.Equal("anna", payload.Issuer);
        Assert.True(TokenGenerator.IsAssisted(payload, json.GetProperty("assist").GetString()));
    }

    private HttpClient NewBrowser() =>
        new(new HttpClientHandler { CookieContainer = new CookieContainer(), AllowAutoRedirect = false }) { BaseAddress = _baseUri };

    private static async Task OptOutAsync(HttpClient browser, string token, string page)
    {
        var field = AntiforgeryField().Match(page);
        Assert.True(field.Success);
        var response = await browser.PostAsync($"/u/{token}",
            new FormUrlEncodedContent([new(field.Groups["name"].Value, field.Groups["value"].Value)]),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task SeedOptedInAsync(params string[] emails)
    {
        using var scope = _app.Services.CreateScope();
        var consent = scope.ServiceProvider.GetRequiredService<ConsentService>();
        foreach (var email in emails)
            await consent.OverrideAsync(Bucket, email, Permission, ConsentStatus.OptedIn);
    }

    private async Task<ConsentAuditEntry> LatestAuditAsync(string email)
    {
        using var scope = _app.Services.CreateScope();
        var hash = scope.ServiceProvider.GetRequiredService<EmailHasher>().Hash(email);
        var audit = await scope.ServiceProvider.GetRequiredService<IConsentRepository>()
            .GetAuditAsync(Bucket, null, 1, 10, TestContext.Current.CancellationToken, [hash]);
        return audit.Records[0];
    }

    [GeneratedRegex("""<input name="(?<name>[^"]+)" type="hidden" value="(?<value>[^"]+)" />""")]
    private static partial Regex AntiforgeryField();
}
