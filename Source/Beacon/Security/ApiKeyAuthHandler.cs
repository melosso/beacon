using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Beacon.Core.Security;
using Beacon.Core.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Beacon.Security;

public class ApiKeyAuthHandler : AuthenticationHandler<ApiKeyAuthOptions>
{
    private const string ApiKeyHeader = "X-Api-Key";
    private static readonly TimeSpan LoginWriteCooldown = TimeSpan.FromMinutes(5);
    private static readonly ConcurrentDictionary<Guid, DateTime> _lastLoginWrite = new();
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
            CryptographicEquals(providedKey, Options.AdminApiKey))
        {
            Logger.LogDebug("Global API key authenticated successfully");
            return BuildSuccess("ApiKeyUser", "admin");
        }

        // 2. Always check per-user API keys when a user repository is available,
        //    regardless of UserAuthentication mode (login UI may be disabled but keys still work)
        using var scope = _scopeFactory.CreateScope();
        var userRepo = scope.ServiceProvider.GetService<IUserRepository>();
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
                var repo = scope.ServiceProvider.GetRequiredService<IUserRepository>();
                await repo.SetLastLoginAsync(userId);
            }
            catch { /* best-effort; next successful auth will retry */ }
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

    private static bool CryptographicEquals(string providedKey, string expectedKey)
    {
        if (providedKey == null || expectedKey == null)
            return false;

        byte[] providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(providedKey));
        byte[] expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expectedKey));

        return CryptographicOperations.FixedTimeEquals(providedHash, expectedHash);
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
