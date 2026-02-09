using System.Security.Cryptography;
using System.Text;
using Beacon.Security;
using Microsoft.Extensions.Options;

namespace Beacon.Api;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/api/admin/auth", Login)
            .ExcludeFromDescription();

        routes.MapPost("/api/admin/auth/refresh", Refresh)
            .RequireAuthorization()
            .ExcludeFromDescription();
    }

    private static IResult Login(
        LoginRequest request,
        IOptionsMonitor<ApiKeyAuthOptions> apiKeyOptions,
        IOptionsMonitor<JwtAuthOptions> jwtOptions)
    {
        if (string.IsNullOrEmpty(request.ApiKey))
            return Results.Json(new { error = "API key is required." }, statusCode: 400);

        var adminApiKey = apiKeyOptions.Get(ApiKeyAuthExtensions.SchemeName).AdminApiKey ?? "";

        if (!CryptographicEquals(request.ApiKey, adminApiKey))
            return Results.Json(new { error = "Invalid API key." }, statusCode: 401);

        var signingKey = jwtOptions.Get(JwtAuthExtensions.SchemeName).SigningKey;

        var expiresAt = DateTimeOffset.UtcNow.AddHours(1);
        var token = JwtAuthHandler.CreateToken(signingKey, "admin", expiresAt);

        return Results.Ok(new { token, expiresAt = expiresAt.ToString("o") });
    }

    private static IResult Refresh(IOptionsMonitor<JwtAuthOptions> jwtOptions)
    {
        var signingKey = jwtOptions.Get(JwtAuthExtensions.SchemeName).SigningKey;

        var expiresAt = DateTimeOffset.UtcNow.AddHours(1);
        var token = JwtAuthHandler.CreateToken(signingKey, "admin", expiresAt);

        return Results.Ok(new { token, expiresAt = expiresAt.ToString("o") });
    }

    private static bool CryptographicEquals(string providedKey, string expectedKey)
    {
        if (providedKey == null || expectedKey == null)
            return false;

        var providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(providedKey));
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expectedKey));

        return CryptographicOperations.FixedTimeEquals(providedHash, expectedHash);
    }

    private record LoginRequest(string ApiKey);
}
