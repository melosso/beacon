using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Beacon.Core.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Beacon.Security;

public class JwtAuthHandler : AuthenticationHandler<JwtAuthOptions>
{
    private readonly IUserRepository? _userRepository;

    public JwtAuthHandler(
        IOptionsMonitor<JwtAuthOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IUserRepository? userRepository = null) : base(options, logger, encoder)
    {
        _userRepository = userRepository;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var authHeader))
            return AuthenticateResult.NoResult();

        var headerValue = authHeader.ToString();
        if (!headerValue.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        var token = headerValue["Bearer ".Length..].Trim();
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

    private static JsonElement? ValidateToken(string token, byte[] signingKey)
    {
        var parts = token.Split('.');
        if (parts.Length != 3)
            return null;

        var signatureInput = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
        using var hmac = new HMACSHA256(signingKey);
        var computedSignature = hmac.ComputeHash(signatureInput);
        var expectedSignature = Base64UrlDecode(parts[2]);

        if (!CryptographicOperations.FixedTimeEquals(computedSignature, expectedSignature))
            return null;

        var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
        return JsonSerializer.Deserialize<JsonElement>(payloadJson);
    }

    public static string CreateToken(byte[] signingKey, string subject, DateTimeOffset expiresAt, string role = "admin")
    {
        var header = Base64UrlEncode("""{"alg":"HS256","typ":"JWT"}"""u8);

        var iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var exp = expiresAt.ToUnixTimeSeconds();
        var payloadJson = $"{{\"sub\":\"{subject}\",\"role\":\"{role}\",\"iat\":{iat},\"exp\":{exp}}}";
        var payload = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));

        var signatureInput = Encoding.ASCII.GetBytes($"{header}.{payload}");
        using var hmac = new HMACSHA256(signingKey);
        var signature = Base64UrlEncode(hmac.ComputeHash(signatureInput));

        return $"{header}.{payload}.{signature}";
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> data)
    {
        return Convert.ToBase64String(data)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var padded = input.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }
        return Convert.FromBase64String(padded);
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
