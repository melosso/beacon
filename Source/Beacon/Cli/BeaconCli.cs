using System.Text;
using System.Text.Json;
using Beacon.Api;
using Beacon.Core.Models;
using Beacon.Core.Security;
using Beacon.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Cli;

public static class BeaconCli
{
    private static readonly string[] Commands = ["accounts", "apikeys", "webhooks", "migrate", "help"];

    public static bool IsCommand(string[] args) => args.Length > 0 && Commands.Contains(args[0]);

    public static async Task<int> RunAsync(string[] args, string provider, string connectionString)
    {
        if (args[0] == "help") return Usage(Console.Out, 0);

        if (provider.Equals("sqlite", StringComparison.OrdinalIgnoreCase)
            && new SqliteConnectionStringBuilder(connectionString).DataSource is { Length: > 0 } file
            && file != ":memory:" && !File.Exists(file) && args[0] != "migrate")
        {
            await Console.Error.WriteLineAsync($"No Beacon database at \"{Path.GetFullPath(file)}\". Run this from the directory Beacon runs in, or set CONNECTION_STRING.");
            return 1;
        }

        var services = new ServiceCollection().AddBeaconStorage(provider, connectionString).BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BeaconDbContext>();
        try
        {
            DatabaseMigrator.Initialize(db);
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"Could not open the database: {ex.Message}");
            return 1;
        }

        return await RunAsync(db, args, Console.In, Console.Out, Console.Error);
    }

    internal static async Task<int> RunAsync(BeaconDbContext db, string[] args, TextReader input, TextWriter output, TextWriter error, CancellationToken ct = default)
    {
        var cli = new Session(db, () => ReadPassword(input, error), output, error, ct);
        return args switch
        {
            ["migrate"] => cli.Done("Database schema is up to date."),
            ["accounts", "list"] => await cli.ListAccountsAsync(),
            ["accounts", "create", var user] => await cli.CreateAccountAsync(user, "admin"),
            ["accounts", "create", var user, var role] => await cli.CreateAccountAsync(user, role),
            ["accounts", "password", var user] => await cli.SetPasswordAsync(user),
            ["accounts", "promote", var user] => await cli.SetRoleAsync(user, "admin"),
            ["accounts", "demote", var user] => await cli.SetRoleAsync(user, "user"),
            ["accounts", "enable", var user] => await cli.SetEnabledAsync(user, true),
            ["accounts", "disable", var user] => await cli.SetEnabledAsync(user, false),
            ["accounts", "delete", var user] => await cli.DeleteAccountAsync(user),
            ["accounts", "apikey", var user] => await cli.RotateAccountKeyAsync(user),
            ["accounts", "providers"] => await cli.ListProvidersAsync(),
            ["accounts", "providers", "enable", var slug] => await cli.SetProviderAsync(slug, true),
            ["accounts", "providers", "disable", var slug] => await cli.SetProviderAsync(slug, false),
            ["accounts", "providers", "delete", var slug] => await cli.DeleteProviderAsync(slug),
            ["apikeys", "list"] => await cli.ListApiKeysAsync(),
            ["apikeys", "enable", var name] => await cli.SetApiKeyAsync(name, true),
            ["apikeys", "disable", var name] => await cli.SetApiKeyAsync(name, false),
            ["apikeys", "delete", var name] => await cli.DeleteApiKeyAsync(name),
            ["webhooks", "list"] => await cli.ListWebhooksAsync(),
            ["webhooks", "enable", var bucket] => await cli.SetWebhookAsync(bucket, true),
            ["webhooks", "disable", var bucket] => await cli.SetWebhookAsync(bucket, false),
            _ => Usage(output, 1),
        };
    }

    public static string Invocation()
    {
        var dll = System.Reflection.Assembly.GetEntryAssembly()?.Location ?? "";
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return "beacon";
        if (dll.Length == 0 || string.Equals(Path.GetFileNameWithoutExtension(exe), Path.GetFileNameWithoutExtension(dll), StringComparison.OrdinalIgnoreCase))
            return Quote(exe);
        return $"dotnet {Quote(dll)}";
    }

    private static string? ReadPassword(TextReader input, TextWriter prompt)
    {
        if (!ReferenceEquals(input, Console.In) || Console.IsInputRedirected) return input.ReadLine();

        prompt.Write("Password: ");
        var password = new StringBuilder();
        for (var key = Console.ReadKey(intercept: true); key.Key != ConsoleKey.Enter; key = Console.ReadKey(intercept: true))
        {
            if (key.Key == ConsoleKey.Backspace) { if (password.Length > 0) password.Length--; }
            else if (!char.IsControl(key.KeyChar)) password.Append(key.KeyChar);
        }
        prompt.WriteLine();
        return password.ToString();
    }

    private static string Quote(string path) => path.Contains(' ') ? $"\"{path}\"" : path;

    private static int Usage(TextWriter output, int exitCode)
    {
        var me = Invocation();
        output.WriteLine("Beacon commands:");
        output.WriteLine($"  {me} accounts list");
        output.WriteLine($"  {me} accounts create <username> [admin|user]");
        output.WriteLine($"  {me} accounts password <username>");
        output.WriteLine($"  {me} accounts promote|demote|enable|disable|delete <username>");
        output.WriteLine($"  {me} accounts apikey <username>");
        output.WriteLine($"  {me} accounts providers");
        output.WriteLine($"  {me} accounts providers enable|disable|delete <key>");
        output.WriteLine($"  {me} apikeys list");
        output.WriteLine($"  {me} apikeys enable|disable|delete <name>");
        output.WriteLine($"  {me} webhooks list");
        output.WriteLine($"  {me} webhooks enable|disable <bucket>");
        output.WriteLine($"  {me} migrate");
        output.WriteLine();
        output.WriteLine("Passwords are read from standard input.");
        output.WriteLine("Reads the same configuration as the server (.env, environment, appsettings.json).");
        return exitCode;
    }

    private sealed class Session(BeaconDbContext db, Func<string?> readPassword, TextWriter output, TextWriter error, CancellationToken ct)
    {
        public int Done(string message)
        {
            output.WriteLine(message);
            return 0;
        }

        private int Fail(string message)
        {
            error.WriteLine(message);
            return 1;
        }

        private async Task<User?> FindAsync(string username)
        {
            var lowered = username.ToLowerInvariant();
            var user = await db.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == lowered, ct);
            if (user is null) Fail($"No account named \"{username}\".");
            return user;
        }

        private Task<bool> IsLastAdminAsync(User user) =>
            user.Role != "admin" || !user.IsEnabled
                ? Task.FromResult(false)
                : db.Users.AllAsync(u => u.Id == user.Id || !u.IsEnabled || u.Role != "admin", ct);

        public async Task<int> ListAccountsAsync()
        {
            var users = await db.Users.AsNoTracking().OrderBy(u => u.Username).ToListAsync(ct);
            if (users.Count == 0)
            {
                output.WriteLine("No accounts. Sign in with ADMIN_API_KEY, or create one with:");
                output.WriteLine($"  {Invocation()} accounts create <username>");
                return 0;
            }

            output.WriteLine($"{"USERNAME",-32} {"ROLE",-6} {"STATE",-9} {"SSO",-12} {"LAST SIGN-IN",-16}");
            foreach (var u in users)
                output.WriteLine($"{u.Username,-32} {u.Role,-6} {(u.IsEnabled ? "enabled" : "disabled"),-9} {u.Provider ?? "-",-12} {u.LastLoginAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "never",-16}");
            return 0;
        }

        public async Task<int> CreateAccountAsync(string username, string role)
        {
            if (!UserEndpoints.ValidateUsername(username, out var nameError)) return Fail(nameError);
            if (role is not ("admin" or "user")) return Fail($"Unknown role \"{role}\". Use admin or user.");
            var lowered = username.ToLowerInvariant();
            if (await db.Users.AnyAsync(u => u.Username.ToLower() == lowered, ct)) return Fail($"\"{username}\" is already taken.");
            var password = readPassword();
            if (!UserEndpoints.ValidatePassword(password, out var passwordError)) return Fail(passwordError);

            var (hash, salt) = PasswordHasher.HashPassword(password);
            var (apiKey, apiKeyHash) = ApiKeyGenerator.Generate();
            db.Users.Add(new User
            {
                Id = Guid.NewGuid(),
                Username = username,
                PasswordHash = hash,
                Salt = salt,
                Role = role,
                ApiKeyHash = apiKeyHash,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync(ct);
            output.WriteLine($"Created {username} as {role}.");
            output.WriteLine($"API key (shown once): {apiKey}");
            return 0;
        }

        public async Task<int> SetPasswordAsync(string username)
        {
            if (await FindAsync(username) is not { } user) return 1;
            var password = readPassword();
            if (!UserEndpoints.ValidatePassword(password, out var passwordError)) return Fail(passwordError);
            (user.PasswordHash, user.Salt) = PasswordHasher.HashPassword(password);
            user.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return Done($"Password set for {user.Username}.");
        }

        public async Task<int> SetRoleAsync(string username, string role)
        {
            if (await FindAsync(username) is not { } user) return 1;
            if (user.Role == role) return Done($"{user.Username} is already {role}.");
            if (role != "admin" && await IsLastAdminAsync(user))
                return Fail($"{user.Username} is the last enabled admin. Promote another account first.");
            user.Role = role;
            user.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return Done($"{user.Username} is now {role}.");
        }

        public async Task<int> SetEnabledAsync(string username, bool enabled)
        {
            if (await FindAsync(username) is not { } user) return 1;
            var state = enabled ? "enabled" : "disabled";
            if (user.IsEnabled == enabled) return Done($"{user.Username} is already {state}.");
            if (!enabled && await IsLastAdminAsync(user))
                return Fail($"{user.Username} is the last enabled admin. Promote another account first.");
            user.IsEnabled = enabled;
            user.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return Done($"{user.Username} is now {state}.");
        }

        public async Task<int> DeleteAccountAsync(string username)
        {
            if (await FindAsync(username) is not { } user) return 1;
            if (await IsLastAdminAsync(user))
                return Fail($"{user.Username} is the last enabled admin. Create another one first.");
            db.Users.Remove(user);
            await db.SaveChangesAsync(ct);
            return Done($"Deleted {user.Username}.");
        }

        public async Task<int> RotateAccountKeyAsync(string username)
        {
            if (await FindAsync(username) is not { } user) return 1;
            var (apiKey, apiKeyHash) = ApiKeyGenerator.Generate();
            user.ApiKeyHash = apiKeyHash;
            user.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            output.WriteLine($"New API key for {user.Username} (shown once): {apiKey}");
            return 0;
        }

        public async Task<int> ListProvidersAsync()
        {
            var providers = await db.OidcProviders.AsNoTracking().OrderBy(p => p.Slug).ToListAsync(ct);
            if (providers.Count == 0) return Done("No sign-in providers. Accounts sign in with a password.");
            output.WriteLine($"{"KEY",-20} {"NAME",-24} {"STATE",-9} {"ISSUER",-40}");
            foreach (var p in providers)
                output.WriteLine($"{p.Slug,-20} {p.Name,-24} {(p.IsEnabled ? "enabled" : "disabled"),-9} {p.Authority,-40}");
            return 0;
        }

        public async Task<int> SetProviderAsync(string slug, bool enabled)
        {
            var provider = await db.OidcProviders.FirstOrDefaultAsync(p => p.Slug == slug, ct);
            if (provider is null) return Fail($"No provider with the key \"{slug}\".");
            provider.IsEnabled = enabled;
            await db.SaveChangesAsync(ct);
            return Done($"{provider.Slug} is now {(enabled ? "enabled" : "disabled")}.");
        }

        public async Task<int> DeleteProviderAsync(string slug)
        {
            var provider = await db.OidcProviders.FirstOrDefaultAsync(p => p.Slug == slug, ct);
            if (provider is null) return Fail($"No provider with the key \"{slug}\".");
            var unbound = await db.Users.Where(u => u.Provider == provider.Slug)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.Provider, (string?)null).SetProperty(u => u.ExternalId, (string?)null), ct);
            db.OidcProviders.Remove(provider);
            await db.SaveChangesAsync(ct);
            output.WriteLine($"Deleted {provider.Slug}. {unbound} account(s) unlinked.");
            if (unbound > 0)
                output.WriteLine($"Accounts created by this provider have no known password. Set one with:  {Invocation()} accounts password <username>");
            return 0;
        }

        private async Task<ApiKey?> FindApiKeyAsync(string nameOrId)
        {
            var matches = Guid.TryParse(nameOrId, out var id)
                ? await db.ApiKeys.Where(k => k.Id == id).ToListAsync(ct)
                : await db.ApiKeys.Where(k => k.Name == nameOrId).ToListAsync(ct);
            if (matches.Count == 1) return matches[0];
            Fail(matches.Count == 0
                ? $"No API key named \"{nameOrId}\"."
                : $"{matches.Count} API keys are named \"{nameOrId}\". Use the ID from \"apikeys list\".");
            return null;
        }

        public async Task<int> ListApiKeysAsync()
        {
            var keys = await db.ApiKeys.AsNoTracking().OrderBy(k => k.Name).ToListAsync(ct);
            if (keys.Count == 0) return Done("No API keys.");
            output.WriteLine($"{"ID",-36} {"NAME",-24} {"STATE",-9} {"PERMISSIONS",-30} {"LAST USED",-16}");
            foreach (var k in keys)
            {
                var permissions = string.Join(",", JsonSerializer.Deserialize<string[]>(k.Permissions) ?? []);
                output.WriteLine($"{k.Id,-36} {k.Name,-24} {(k.IsEnabled ? "enabled" : "disabled"),-9} {permissions,-30} {k.LastUsedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "never",-16}");
            }
            return 0;
        }

        public async Task<int> SetApiKeyAsync(string nameOrId, bool enabled)
        {
            if (await FindApiKeyAsync(nameOrId) is not { } key) return 1;
            key.IsEnabled = enabled;
            key.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return Done($"{key.Name} is now {(enabled ? "enabled" : "disabled")}.");
        }

        public async Task<int> DeleteApiKeyAsync(string nameOrId)
        {
            if (await FindApiKeyAsync(nameOrId) is not { } key) return 1;
            db.ApiKeys.Remove(key);
            await db.SaveChangesAsync(ct);
            return Done($"Deleted API key {key.Name}.");
        }

        public async Task<int> ListWebhooksAsync()
        {
            var hooks = await db.WebhookConfigs.AsNoTracking().OrderBy(w => w.Bucket).ToListAsync(ct);
            if (hooks.Count == 0) return Done("No webhooks.");
            output.WriteLine($"{"BUCKET",-32} {"STATE",-9} {"SENT",8} {"LAST SENT",-16}");
            foreach (var w in hooks)
                output.WriteLine($"{w.Bucket,-32} {(w.IsEnabled ? "enabled" : "disabled"),-9} {w.TriggerCount,8} {w.LastTriggeredAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "never",-16}");
            return 0;
        }

        public async Task<int> SetWebhookAsync(string bucket, bool enabled)
        {
            var normalized = bucket.Trim().ToLowerInvariant();
            var hook = await db.WebhookConfigs.FirstOrDefaultAsync(w => w.Bucket == normalized, ct);
            if (hook is null) return Fail($"No webhook for bucket \"{bucket}\".");
            hook.IsEnabled = enabled;
            await db.SaveChangesAsync(ct);
            return Done($"Webhook for {hook.Bucket} is now {(enabled ? "enabled" : "disabled")}.");
        }
    }
}
