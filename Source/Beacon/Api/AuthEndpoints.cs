using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Beacon.Core.Security;
using Beacon.Core.Services;
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

    private static async Task<IResult> Login(
        LoginRequest request,
        IOptionsMonitor<ApiKeyAuthOptions> apiKeyOptions,
        IOptionsMonitor<JwtAuthOptions> jwtOptions,
        IUserRepository? userRepo = null)
    {
        var opts = apiKeyOptions.Get(ApiKeyAuthExtensions.SchemeName);
        var userAuthentication = opts.UserAuthentication ?? "";
        var signingKey = jwtOptions.Get(JwtAuthExtensions.SchemeName).SigningKey;
        var expiresAt = DateTimeOffset.UtcNow.AddHours(1);

        if (userAuthentication == "user")
        {
            // Username + password login only
            if (string.IsNullOrEmpty(request.Username) || string.IsNullOrEmpty(request.Password))
                return Results.Json(new { error = "Invalid credentials." }, statusCode: 401);

            if (userRepo == null)
                return Results.Json(new { error = "User authentication not configured." }, statusCode: 500);

            var user = await userRepo.FindByUsernameAsync(request.Username);
            if (user == null || !user.IsEnabled ||
                !PasswordHasher.VerifyPassword(request.Password, user.PasswordHash, user.Salt))
            {
                return Results.Json(new { error = "Invalid credentials." }, statusCode: 401);
            }

            await userRepo.SetLastLoginAsync(user.Id);
            var token = JwtAuthHandler.CreateToken(signingKey, user.Username, expiresAt, user.Role);
            return Results.Ok(new { token, expiresAt = expiresAt.ToString("o"), role = user.Role });
        }

        if (userAuthentication == "api")
        {
            // Per-user API key login only
            if (string.IsNullOrEmpty(request.ApiKey))
                return Results.Json(new { error = "Invalid credentials." }, statusCode: 401);

            // Check global AdminApiKey first
            var adminApiKey = opts.AdminApiKey ?? "";
            if (!string.IsNullOrEmpty(adminApiKey) && CryptographicEquals(request.ApiKey, adminApiKey))
            {
                var token = JwtAuthHandler.CreateToken(signingKey, "admin", expiresAt, "admin");
                return Results.Ok(new { token, expiresAt = expiresAt.ToString("o"), role = "admin" });
            }

            // Check per-user API keys
            if (userRepo != null)
            {
                var keyHash = ApiKeyGenerator.ComputeHash(request.ApiKey);
                var user = await userRepo.FindByApiKeyHashAsync(keyHash);
                if (user != null && user.IsEnabled)
                {
                    await userRepo.SetLastLoginAsync(user.Id);
                    var token = JwtAuthHandler.CreateToken(signingKey, user.Username, expiresAt, user.Role);
                    return Results.Ok(new { token, expiresAt = expiresAt.ToString("o"), role = user.Role });
                }
            }

            return Results.Json(new { error = "Invalid credentials." }, statusCode: 401);
        }

        if (userAuthentication == "both")
        {
            if (userRepo == null)
                return Results.Json(new { error = "User authentication not configured." }, statusCode: 500);

            // Try username + password if provided
            if (!string.IsNullOrEmpty(request.Username) && !string.IsNullOrEmpty(request.Password))
            {
                var user = await userRepo.FindByUsernameAsync(request.Username);
                if (user != null && user.IsEnabled &&
                    PasswordHasher.VerifyPassword(request.Password, user.PasswordHash, user.Salt))
                {
                    await userRepo.SetLastLoginAsync(user.Id);
                    var token = JwtAuthHandler.CreateToken(signingKey, user.Username, expiresAt, user.Role);
                    return Results.Ok(new { token, expiresAt = expiresAt.ToString("o"), role = user.Role });
                }
            }

            // Try API key if provided
            if (!string.IsNullOrEmpty(request.ApiKey))
            {
                var adminApiKey = opts.AdminApiKey ?? "";
                if (!string.IsNullOrEmpty(adminApiKey) && CryptographicEquals(request.ApiKey, adminApiKey))
                {
                    var token = JwtAuthHandler.CreateToken(signingKey, "admin", expiresAt, "admin");
                    return Results.Ok(new { token, expiresAt = expiresAt.ToString("o"), role = "admin" });
                }

                var keyHash = ApiKeyGenerator.ComputeHash(request.ApiKey);
                var userByKey = await userRepo.FindByApiKeyHashAsync(keyHash);
                if (userByKey != null && userByKey.IsEnabled)
                {
                    await userRepo.SetLastLoginAsync(userByKey.Id);
                    var token = JwtAuthHandler.CreateToken(signingKey, userByKey.Username, expiresAt, userByKey.Role);
                    return Results.Ok(new { token, expiresAt = expiresAt.ToString("o"), role = userByKey.Role });
                }
            }

            return Results.Json(new { error = "Invalid credentials." }, statusCode: 401);
        }

        // Legacy mode: global AdminApiKey only
        if (string.IsNullOrEmpty(request.ApiKey))
            return Results.Json(new { error = "API key is required." }, statusCode: 400);

        var legacyAdminKey = opts.AdminApiKey ?? "";
        if (!CryptographicEquals(request.ApiKey, legacyAdminKey))
            return Results.Json(new { error = "Invalid API key." }, statusCode: 401);

        var legacyToken = JwtAuthHandler.CreateToken(signingKey, "admin", expiresAt, "admin");
        return Results.Ok(new { token = legacyToken, expiresAt = expiresAt.ToString("o"), role = "admin" });
    }

    private static IResult Refresh(HttpContext httpContext, IOptionsMonitor<JwtAuthOptions> jwtOptions)
    {
        var signingKey = jwtOptions.Get(JwtAuthExtensions.SchemeName).SigningKey;

        var sub = httpContext.User.FindFirstValue(ClaimTypes.Name) ?? "admin";
        var role = httpContext.User.FindFirstValue(ClaimTypes.Role) ?? "admin";

        var expiresAt = DateTimeOffset.UtcNow.AddHours(1);
        var token = JwtAuthHandler.CreateToken(signingKey, sub, expiresAt, role);

        return Results.Ok(new { token, expiresAt = expiresAt.ToString("o"), role });
    }

    private static bool CryptographicEquals(string providedKey, string expectedKey)
    {
        if (providedKey == null || expectedKey == null)
            return false;

        var providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(providedKey));
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expectedKey));

        return CryptographicOperations.FixedTimeEquals(providedHash, expectedHash);
    }

    private record LoginRequest(string? ApiKey = null, string? Username = null, string? Password = null);
}
