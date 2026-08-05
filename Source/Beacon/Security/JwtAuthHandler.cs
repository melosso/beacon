using System.Buffers.Text;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Beacon.Core.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Beacon.Storage;

namespace Beacon.Security;

public class JwtAuthHandler : AuthenticationHandler<JwtAuthOptions>
{
    private readonly UserRepository? _userRepository;

    public JwtAuthHandler(
        IOptionsMonitor<JwtAuthOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        UserRepository? userRepository = null) : base(options, logger, encoder)
    {
        _userRepository = userRepository;
    }

    public const string CookieName = "beacon_auth";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Prefer Bearer token from Authorization header; fall back to HttpOnly cookie
        string? token = null;
        if (Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            var headerValue = authHeader.ToString();
            if (headerValue.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                token = headerValue["Bearer ".Length..].Trim();
        }

        if (string.IsNullOrEmpty(token))
            Request.Cookies.TryGetValue(CookieName, out token);

        if (string.IsNullOrEmpty(token))
            return AuthenticateResult.NoResult();

        try
        {
            var payloadResult = ValidateToken(token, Options.SigningKey);
            if (payloadResult == null)
                return AuthenticateResult.Fail("Invalid token signature");

            var payload = payloadResult.Value;
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            if (payload.TryGetProperty("nbf", out var nbfElement))
            {
                if (now < nbfElement.GetInt64())
                    return AuthenticateResult.Fail("Token not yet valid");
            }

            if (payload.TryGetProperty("exp", out var expElement))
            {
                if (now >= expElement.GetInt64())
                    return AuthenticateResult.Fail("Token expired");
            }
            else
            {
                return AuthenticateResult.Fail("Token missing expiry");
            }

            var sub = payload.TryGetProperty("sub", out var subElement) ? subElement.GetString() : null;
            if (string.IsNullOrEmpty(sub))
                return AuthenticateResult.Fail("Token missing subject");

            if (!payload.TryGetProperty("role", out var roleElement))
                return AuthenticateResult.Fail("Token missing role claim");

            var role = roleElement.GetString();
            if (string.IsNullOrEmpty(role))
                return AuthenticateResult.Fail("Token missing role claim");

            // When user authentication is enabled, verify per-user tokens against the database
            // so that disabled accounts and role changes take effect immediately.
            // If no user record exists for the subject, the token was issued for the global
            // AdminApiKey (which has no DB record), so it is allowed through unchanged.
            if (!string.IsNullOrEmpty(Options.UserAuthentication) && _userRepository != null)
            {
                var user = await _userRepository.FindByUsernameAsync(sub);
                if (user != null)
                {
                    if (!user.IsEnabled)
                        return AuthenticateResult.Fail("User is disabled");
                    role = user.Role;
                }
            }

            var claims = new[]
            {
                new Claim(ClaimTypes.Name, sub),
                new Claim(ClaimTypes.Role, role)
            };
            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);

            return AuthenticateResult.Success(ticket);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "JWT validation failed");
            return AuthenticateResult.Fail("Invalid token");
        }
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

    /// <summary>
    /// Validates signature and expiry without DB lookups. Used for lightweight page-serve checks.
    /// </summary>
    public static bool TryValidateToken(byte[] signingKey, string token,
        out string? subject, out string? role, out DateTimeOffset? expiresAt)
    {
        subject = null; role = null; expiresAt = null;
        try
        {
            var payload = ValidateToken(token, signingKey);
            if (payload == null) return false;

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (!payload.Value.TryGetProperty("exp", out var expEl)) return false;
            var expSeconds = expEl.GetInt64();
            if (now >= expSeconds) return false;
            expiresAt = DateTimeOffset.FromUnixTimeSeconds(expSeconds);

            subject = payload.Value.TryGetProperty("sub", out var subEl) ? subEl.GetString() : null;
            role    = payload.Value.TryGetProperty("role", out var roleEl) ? roleEl.GetString() : null;
            return !string.IsNullOrEmpty(subject);
        }
        catch { return false; }
    }

    private static JsonElement? ValidateToken(string token, byte[] signingKey)
    {
        var parts = token.Split('.');
        if (parts.Length != 3)
            return null;

        var signatureInput = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
        using var hmac = new HMACSHA256(signingKey);
        var computedSignature = hmac.ComputeHash(signatureInput);
        var expectedSignature = Base64Url.DecodeFromChars(parts[2]);

        if (!CryptographicOperations.FixedTimeEquals(computedSignature, expectedSignature))
            return null;

        var payloadJson = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(parts[1]));
        return JsonSerializer.Deserialize<JsonElement>(payloadJson);
    }

    public static string CreateToken(byte[] signingKey, string subject, DateTimeOffset expiresAt, string role = "admin")
    {
        var header = Base64Url.EncodeToString("""{"alg":"HS256","typ":"JWT"}"""u8);

        // Serialize, never interpolate: an unescaped quote in the subject would inject payload claims.
        var payload = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(new
        {
            sub = subject,
            role,
            iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            exp = expiresAt.ToUnixTimeSeconds()
        }));

        var signatureInput = Encoding.ASCII.GetBytes($"{header}.{payload}");
        using var hmac = new HMACSHA256(signingKey);
        var signature = Base64Url.EncodeToString(hmac.ComputeHash(signatureInput));

        return $"{header}.{payload}.{signature}";
    }
}

public class JwtAuthOptions : AuthenticationSchemeOptions
{
    public byte[] SigningKey { get; set; } = [];
    public string UserAuthentication { get; set; } = "";
}

public static class JwtAuthExtensions
{
    public const string SchemeName = "Jwt";

    public static AuthenticationBuilder AddJwtAuth(
        this AuthenticationBuilder builder,
        Action<JwtAuthOptions> configureOptions)
    {
        return builder.AddScheme<JwtAuthOptions, JwtAuthHandler>(
            SchemeName, configureOptions);
    }
}
