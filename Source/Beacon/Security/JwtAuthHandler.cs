using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Beacon.Security;

public class JwtAuthHandler : AuthenticationHandler<JwtAuthOptions>
{
    public JwtAuthHandler(
        IOptionsMonitor<JwtAuthOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var authHeader))
            return Task.FromResult(AuthenticateResult.NoResult());

        var headerValue = authHeader.ToString();
        if (!headerValue.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(AuthenticateResult.NoResult());

        var token = headerValue["Bearer ".Length..].Trim();
        if (string.IsNullOrEmpty(token))
            return Task.FromResult(AuthenticateResult.NoResult());

        try
        {
            var payloadResult = ValidateToken(token, Options.SigningKey);
            if (payloadResult == null)
                return Task.FromResult(AuthenticateResult.Fail("Invalid token signature"));

            var payload = payloadResult.Value;

            if (payload.TryGetProperty("exp", out var expElement))
            {
                var exp = expElement.GetInt64();
                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                if (now >= exp)
                    return Task.FromResult(AuthenticateResult.Fail("Token expired"));
            }
            else
            {
                return Task.FromResult(AuthenticateResult.Fail("Token missing expiry"));
            }

            var sub = payload.TryGetProperty("sub", out var subElement) ? subElement.GetString() ?? "unknown" : "unknown";
            var role = payload.TryGetProperty("role", out var roleElement) ? roleElement.GetString() ?? "admin" : "admin";

            var claims = new[]
            {
                new Claim(ClaimTypes.Name, sub),
                new Claim(ClaimTypes.Role, role)
            };
            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "JWT validation failed");
            return Task.FromResult(AuthenticateResult.Fail("Invalid token"));
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
