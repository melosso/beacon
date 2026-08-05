using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Beacon.Core.Security;
using Beacon.Core.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Beacon.Storage;

namespace Beacon.Security;

public class ApiKeyAuthHandler : AuthenticationHandler<ApiKeyAuthOptions>
{
    private const string ApiKeyHeader = "X-Api-Key";
    private static readonly TimeSpan LoginWriteCooldown = TimeSpan.FromMinutes(5);
    private static readonly ConcurrentDictionary<Guid, DateTime> _lastLoginWrite = new();
    private static readonly ConcurrentDictionary<Guid, DateTime> _lastKeyUsedWrite = new();
    private readonly IServiceScopeFactory _scopeFactory;

    public ApiKeyAuthHandler(
        IOptionsMonitor<ApiKeyAuthOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IServiceScopeFactory scopeFactory) : base(options, logger, encoder)
    {
        _scopeFactory = scopeFactory;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // No API key header present
        if (!Request.Headers.TryGetValue(ApiKeyHeader, out var apiKeyHeader))
            return AuthenticateResult.NoResult();

        var providedKey = apiKeyHeader.ToString();

        // Strip "Bearer " prefix if callers mistakenly include it
        if (providedKey.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            providedKey = providedKey["Bearer ".Length..];

        // 1. Always check global AdminApiKey first (constant-time SHA256 comparison)
        if (!string.IsNullOrEmpty(Options.AdminApiKey) &&
            ApiKeyGenerator.SecretEquals(providedKey, Options.AdminApiKey))
        {
            Logger.LogDebug("Global API key authenticated successfully");
            return BuildSuccess("ApiKeyUser", "admin");
        }

        // 2. Always check per-user API keys when a user repository is available,
        //    regardless of UserAuthentication mode (login UI may be disabled but keys still work)
        using var scope = _scopeFactory.CreateScope();
        var userRepo = scope.ServiceProvider.GetService<UserRepository>();
        if (userRepo != null)
        {
            var keyHash = ApiKeyGenerator.ComputeHash(providedKey);
            var user = await userRepo.FindByApiKeyHashAsync(keyHash);
            if (user != null && user.IsEnabled)
            {
                Logger.LogDebug("User API key authenticated for {Username}", user.Username);
                UpdateLastLoginWithCooldown(user.Id);
                return BuildSuccess(user.Username, user.Role);
            }
        }

        // 3. Check named API keys table (RBAC with optional validity window)
        var apiKeyRepo = scope.ServiceProvider.GetService<ApiKeyRepository>();
        if (apiKeyRepo != null)
        {
            var keyHash = ApiKeyGenerator.ComputeHash(providedKey);
            var apiKey = await apiKeyRepo.FindByKeyHashAsync(keyHash);
            if (apiKey != null && apiKey.IsEnabled)
            {
                var now = DateTime.UtcNow;
                if (apiKey.ActiveFrom.HasValue && now < apiKey.ActiveFrom.Value)
                    return AuthenticateResult.Fail("API key not yet active");
                if (apiKey.ActiveUntil.HasValue && now > apiKey.ActiveUntil.Value)
                    return AuthenticateResult.Fail("API key expired");

                var perms = JsonSerializer.Deserialize<string[]>(apiKey.Permissions) ?? [];
                if (perms.Contains("_none"))
                    return AuthenticateResult.Fail("API key has no permissions");

                Logger.LogDebug("Named API key authenticated: {Name}", apiKey.Name);
                UpdateLastKeyUsedWithCooldown(apiKey.Id);
                return BuildSuccessWithPermissions(apiKey.Name, perms);
            }
        }

        Logger.LogWarning("Invalid API key attempt from {RemoteIp}", Context.Connection.RemoteIpAddress);
        return AuthenticateResult.Fail("Invalid API key");
    }

    private void UpdateLastLoginWithCooldown(Guid userId)
    {
        var now = DateTime.UtcNow;
        if (_lastLoginWrite.TryGetValue(userId, out var last) && now - last < LoginWriteCooldown)
            return;

        _lastLoginWrite[userId] = now;
        var scopeFactory = _scopeFactory;
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<UserRepository>();
                await repo.SetLastLoginAsync(userId);
            }
            catch (Exception ex) { Logger.LogDebug(ex, "best-effort last-login update failed; will retry on next auth"); }
        });
    }

    private void UpdateLastKeyUsedWithCooldown(Guid keyId)
    {
        var now = DateTime.UtcNow;
        if (_lastKeyUsedWrite.TryGetValue(keyId, out var last) && now - last < LoginWriteCooldown)
            return;

        _lastKeyUsedWrite[keyId] = now;
        var scopeFactory = _scopeFactory;
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var r = scope.ServiceProvider.GetRequiredService<ApiKeyRepository>();
                await r.UpdateLastUsedAsync(keyId);
            }
            catch (Exception ex) { Logger.LogDebug(ex, "best-effort last-key-used update failed"); }
        });
    }

    private AuthenticateResult BuildSuccess(string name, string role)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, name),
            new Claim(ClaimTypes.Role, role)
        };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);
        return AuthenticateResult.Success(ticket);
    }

    private AuthenticateResult BuildSuccessWithPermissions(string name, string[] permissions)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, name),
            new(ClaimTypes.Role, "apikey")
        };
        foreach (var p in permissions)
            claims.Add(new Claim("beacon:permission", p));

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);
        return AuthenticateResult.Success(ticket);
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = 401;
        Response.ContentType = "application/json";
        await Response.WriteAsync("{\"error\":\"Unauthorized.\"}");
        await Response.CompleteAsync();
    }

    protected override async Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = 403;
        Response.ContentType = "application/json";
        await Response.WriteAsync("{\"error\":\"Forbidden. Insufficient permissions.\"}");
        await Response.CompleteAsync();
    }
}

public class ApiKeyAuthOptions : AuthenticationSchemeOptions
{
    public string? AdminApiKey { get; set; }
    public string UserAuthentication { get; set; } = "";
}

public static class ApiKeyAuthExtensions
{
    public const string SchemeName = "ApiKey";

    public static AuthenticationBuilder AddApiKeyAuth(
        this AuthenticationBuilder builder,
        Action<ApiKeyAuthOptions> configureOptions)
    {
        return builder.AddScheme<ApiKeyAuthOptions, ApiKeyAuthHandler>(
            SchemeName, configureOptions);
    }
}
