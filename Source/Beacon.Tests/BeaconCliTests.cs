using Beacon.Cli;
using Beacon.Core.Models;
using Beacon.Core.Security;
using Beacon.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Beacon.Tests;

public sealed class BeaconCliTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly BeaconDbContext _db;

    public BeaconCliTests()
    {
        _connection.Open();
        _db = new BeaconDbContext(new DbContextOptionsBuilder<BeaconDbContext>().UseSqlite(_connection).Options);
        DatabaseMigrator.Initialize(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private Task<(int Code, string Output, string Error)> Run(params string[] args) => RunWithInput("", args);

    private async Task<(int Code, string Output, string Error)> RunWithInput(string input, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var code = await BeaconCli.RunAsync(_db, args, new StringReader(input), output, error, TestContext.Current.CancellationToken);
        _db.ChangeTracker.Clear();
        return (code, output.ToString(), error.ToString());
    }

    [Fact]
    public async Task Accounts_CreateValidatesAndPrintsKeyOnce()
    {
        var created = await RunWithInput("correct-horse-battery", "accounts", "create", "alice");
        var duplicate = await RunWithInput("correct-horse-battery", "accounts", "create", "ALICE");
        var weak = await RunWithInput("short", "accounts", "create", "bob");
        var role = await RunWithInput("correct-horse-battery", "accounts", "create", "bob", "owner");
        var argvPassword = await Run("accounts", "create", "bob", "correct-horse-battery");
        var list = await Run("accounts", "list");

        Assert.Equal(0, created.Code);
        Assert.Contains("API key (shown once): ", created.Output);
        Assert.Equal((1, 1, 1, 1), (duplicate.Code, weak.Code, role.Code, argvPassword.Code));
        Assert.Contains("alice", list.Output);
        Assert.DoesNotContain("bob", list.Output);
        var alice = await _db.Users.SingleAsync(TestContext.Current.CancellationToken);
        var key = created.Output.Split("API key (shown once): ")[1].Trim();
        Assert.Equal(ApiKeyGenerator.ComputeHash(key), alice.ApiKeyHash);
    }

    [Fact]
    public async Task Accounts_PasswordAndKeyRotation()
    {
        await RunWithInput("correct-horse-battery", "accounts", "create", "alice");
        var before = (await _db.Users.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken)).ApiKeyHash;

        Assert.Equal(0, (await RunWithInput("another-long-password", "accounts", "password", "alice")).Code);
        var rotated = await Run("accounts", "apikey", "alice");
        Assert.Equal(1, (await RunWithInput("another-long-password", "accounts", "password", "nobody")).Code);

        var alice = await _db.Users.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.True(PasswordHasher.VerifyPassword("another-long-password", alice.PasswordHash, alice.Salt));
        Assert.NotEqual(before, alice.ApiKeyHash);
        Assert.Contains("(shown once)", rotated.Output);
    }

    [Fact]
    public async Task Accounts_LastAdminCannotBeDemotedDisabledOrDeleted()
    {
        await RunWithInput("correct-horse-battery", "accounts", "create", "alice");

        Assert.Equal(1, (await Run("accounts", "demote", "alice")).Code);
        Assert.Equal(1, (await Run("accounts", "disable", "alice")).Code);
        Assert.Equal(1, (await Run("accounts", "delete", "alice")).Code);

        await RunWithInput("correct-horse-battery", "accounts", "create", "bob", "user");
        Assert.Equal(0, (await Run("accounts", "promote", "bob")).Code);
        Assert.Equal(0, (await Run("accounts", "demote", "alice")).Code);
        Assert.Equal(0, (await Run("accounts", "disable", "alice")).Code);
        Assert.Equal(0, (await Run("accounts", "delete", "alice")).Code);
        Assert.Equal("bob", (await _db.Users.SingleAsync(TestContext.Current.CancellationToken)).Username);
    }

    [Fact]
    public async Task Providers_DisableAndDeleteUnlinksAccounts()
    {
        await RunWithInput("correct-horse-battery", "accounts", "create", "alice");
        var alice = await _db.Users.SingleAsync(TestContext.Current.CancellationToken);
        alice.Provider = "idp";
        alice.ExternalId = "sub";
        _db.OidcProviders.Add(new OidcProvider { Slug = "idp", Name = "IdP", Authority = "https://idp.example.test", ClientId = "c", IsEnabled = true });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);
        _db.ChangeTracker.Clear();

        Assert.Equal(0, (await Run("accounts", "providers", "disable", "idp")).Code);
        Assert.False((await _db.OidcProviders.SingleAsync(TestContext.Current.CancellationToken)).IsEnabled);
        var deleted = await Run("accounts", "providers", "delete", "idp");

        Assert.Contains("1 account(s) unlinked", deleted.Output);
        Assert.Null((await _db.Users.SingleAsync(TestContext.Current.CancellationToken)).Provider);
        Assert.Equal(1, (await Run("accounts", "providers", "delete", "idp")).Code);
    }

    [Fact]
    public async Task ApiKeys_ToggleAndDeleteByNameOrId()
    {
        _db.ApiKeys.AddRange(
            new ApiKey { Id = Guid.NewGuid(), Name = "crm", KeyHash = "h1", Permissions = "[\"consent:read\"]", CreatedAt = DateTime.UtcNow },
            new ApiKey { Id = Guid.NewGuid(), Name = "dup", KeyHash = "h2", Permissions = "[]", CreatedAt = DateTime.UtcNow },
            new ApiKey { Id = Guid.NewGuid(), Name = "dup", KeyHash = "h3", Permissions = "[]", CreatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);
        _db.ChangeTracker.Clear();

        Assert.Equal(0, (await Run("apikeys", "disable", "crm")).Code);
        Assert.False((await _db.ApiKeys.SingleAsync(k => k.Name == "crm", TestContext.Current.CancellationToken)).IsEnabled);
        Assert.Contains("Use the ID", (await Run("apikeys", "delete", "dup")).Error);
        var id = (await _db.ApiKeys.FirstAsync(k => k.Name == "dup", TestContext.Current.CancellationToken)).Id;
        Assert.Equal(0, (await Run("apikeys", "delete", id.ToString())).Code);
        Assert.Contains("consent:read", (await Run("apikeys", "list")).Output);
        Assert.Equal(2, await _db.ApiKeys.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Webhooks_DisableAndEnable()
    {
        _db.WebhookConfigs.Add(new WebhookConfig { Id = Guid.NewGuid(), Bucket = "news", EncryptedUrl = "u", EncryptedMethod = "m" });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);
        _db.ChangeTracker.Clear();

        Assert.Equal(0, (await Run("webhooks", "disable", "NEWS")).Code);
        Assert.False((await _db.WebhookConfigs.SingleAsync(TestContext.Current.CancellationToken)).IsEnabled);
        Assert.Equal(0, (await Run("webhooks", "enable", "news")).Code);
        Assert.True((await _db.WebhookConfigs.SingleAsync(TestContext.Current.CancellationToken)).IsEnabled);
        Assert.Equal(1, (await Run("webhooks", "disable", "missing")).Code);
    }

    [Fact]
    public async Task UnknownCommand_PrintsUsage()
    {
        var result = await Run("accounts", "frobnicate");

        Assert.Equal(1, result.Code);
        Assert.Contains("accounts create", result.Output);
        Assert.True(BeaconCli.IsCommand(["accounts"]));
        Assert.False(BeaconCli.IsCommand(["--urls", "http://*:80"]));
    }
}
