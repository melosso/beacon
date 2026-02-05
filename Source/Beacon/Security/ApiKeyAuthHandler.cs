using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Beacon.Security;

public class ApiKeyAuthHandler : AuthenticationHandler<ApiKeyAuthOptions>
{
    private const string ApiKeyHeader = "X-Api-Key";

    public ApiKeyAuthHandler(
        IOptionsMonitor<ApiKeyAuthOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // No API key header present - this is normal for unauthenticated endpoints
        // Use NoResult() to indicate this scheme doesn't apply (not a failure)
        if (!Request.Headers.TryGetValue(ApiKeyHeader, out var apiKeyHeader))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var providedKey = apiKeyHeader.ToString();

        if (string.IsNullOrEmpty(Options.AdminApiKey) ||
            !CryptographicEquals(providedKey, Options.AdminApiKey))
        {
            Logger.LogWarning("Invalid API key attempt from {RemoteIp}",
                Context.Connection.RemoteIpAddress);
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key"));
        }

        Logger.LogDebug("API key authenticated successfully");

        var claims = new[] { new Claim(ClaimTypes.Name, "ApiKeyUser") };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
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
        {
            return false;
        }

        // Use a fixed-length representation to mask the actual key length
        byte[] providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(providedKey));
        byte[] expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expectedKey));

        // FixedTimeEquals ensures the bitwise comparison time is constant
        return CryptographicOperations.FixedTimeEquals(providedHash, expectedHash);
    }
}

public class ApiKeyAuthOptions : AuthenticationSchemeOptions
{
    public string? AdminApiKey { get; set; }
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
