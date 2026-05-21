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

        // No auth required, always safe to clear the cookie
        routes.MapPost("/api/admin/auth/logout", Logout)
            .ExcludeFromDescription();

        routes.MapGet("/api/admin/auth/me", Me)
            .RequireAuthorization()
            .ExcludeFromDescription();
    }

    private static async Task<IResult> Login(
        LoginRequest request,
        HttpContext httpContext,
        IWebHostEnvironment env,
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
            if (string.IsNullOrEmpty(request.Username) || string.IsNullOrEmpty(request.Password))
                return Results.Json(new { error = "Invalid credentials." }, statusCode: 401);

            if (userRepo == null)
                return Results.Json(new { error = "User authentication not configured." }, statusCode: 500);

            var user = await userRepo.FindByUsernameAsync(request.Username);
            if (user == null || !user.IsEnabled ||
                !PasswordHasher.VerifyPassword(request.Password, user.PasswordHash, user.Salt))
                return Results.Json(new { error = "Invalid credentials." }, statusCode: 401);

            await userRepo.SetLastLoginAsync(user.Id);
            return OkWithCookie(httpContext, env, signingKey, user.Username, expiresAt, user.Role);
        }

        if (userAuthentication == "api")
        {
            if (string.IsNullOrEmpty(request.ApiKey))
                return Results.Json(new { error = "Invalid credentials." }, statusCode: 401);

            var adminApiKey = opts.AdminApiKey ?? "";
            if (!string.IsNullOrEmpty(adminApiKey) && CryptographicEquals(request.ApiKey, adminApiKey))
                return OkWithCookie(httpContext, env, signingKey, "admin", expiresAt, "admin");

            if (userRepo != null)
            {
                var keyHash = ApiKeyGenerator.ComputeHash(request.ApiKey);
                var user = await userRepo.FindByApiKeyHashAsync(keyHash);
                if (user != null && user.IsEnabled)
                {
                    await userRepo.SetLastLoginAsync(user.Id);
                    return OkWithCookie(httpContext, env, signingKey, user.Username, expiresAt, user.Role);
                }
            }

            return Results.Json(new { error = "Invalid credentials." }, statusCode: 401);
        }

        if (userAuthentication == "both")
        {
            if (userRepo == null)
                return Results.Json(new { error = "User authentication not configured." }, statusCode: 500);

            if (!string.IsNullOrEmpty(request.Username) && !string.IsNullOrEmpty(request.Password))
            {
                var user = await userRepo.FindByUsernameAsync(request.Username);
                if (user != null && user.IsEnabled &&
                    PasswordHasher.VerifyPassword(request.Password, user.PasswordHash, user.Salt))
                {
                    await userRepo.SetLastLoginAsync(user.Id);
                    return OkWithCookie(httpContext, env, signingKey, user.Username, expiresAt, user.Role);
                }
            }

            if (!string.IsNullOrEmpty(request.ApiKey))
            {
                var adminApiKey = opts.AdminApiKey ?? "";
                if (!string.IsNullOrEmpty(adminApiKey) && CryptographicEquals(request.ApiKey, adminApiKey))
                    return OkWithCookie(httpContext, env, signingKey, "admin", expiresAt, "admin");

                var keyHash = ApiKeyGenerator.ComputeHash(request.ApiKey);
                var userByKey = await userRepo.FindByApiKeyHashAsync(keyHash);
                if (userByKey != null && userByKey.IsEnabled)
                {
                    await userRepo.SetLastLoginAsync(userByKey.Id);
                    return OkWithCookie(httpContext, env, signingKey, userByKey.Username, expiresAt, userByKey.Role);
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

        return OkWithCookie(httpContext, env, signingKey, "admin", expiresAt, "admin");
    }

    private static IResult Refresh(
        HttpContext httpContext,
        IWebHostEnvironment env,
        IOptionsMonitor<JwtAuthOptions> jwtOptions)
    {
        var signingKey = jwtOptions.Get(JwtAuthExtensions.SchemeName).SigningKey;
        var sub  = httpContext.User.FindFirstValue(ClaimTypes.Name) ?? "admin";
        var role = httpContext.User.FindFirstValue(ClaimTypes.Role) ?? "admin";
        return OkWithCookie(httpContext, env, signingKey, sub, DateTimeOffset.UtcNow.AddHours(1), role);
    }

    private static IResult Logout(HttpContext httpContext)
    {
        // Expire the auth cookie
        httpContext.Response.Cookies.Append(JwtAuthHandler.CookieName, "", new CookieOptions
        {
            HttpOnly = true,
            Secure   = !httpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment(),
            SameSite = SameSiteMode.Lax,
            Path     = "/",
            Expires  = DateTimeOffset.UnixEpoch
        });
        return Results.NoContent();
    }

    private static IResult Me(HttpContext httpContext, IOptionsMonitor<JwtAuthOptions> jwtOptions)
    {
        var sub  = httpContext.User.FindFirstValue(ClaimTypes.Name) ?? "admin";
        var role = httpContext.User.FindFirstValue(ClaimTypes.Role) ?? "admin";

        DateTimeOffset? expiresAt = null;
        if (httpContext.Request.Cookies.TryGetValue(JwtAuthHandler.CookieName, out var token))
        {
            var key = jwtOptions.Get(JwtAuthExtensions.SchemeName).SigningKey;
            JwtAuthHandler.TryValidateToken(key, token, out _, out _, out expiresAt);
        }

        return Results.Ok(new { role, username = sub, expiresAt = expiresAt?.ToString("o") });
    }

    // Sets the HttpOnly auth cookie and returns {role, username, expiresAt}
    private static IResult OkWithCookie(
        HttpContext httpContext,
        IWebHostEnvironment env,
        byte[] signingKey,
        string subject,
        DateTimeOffset expiresAt,
        string role)
    {
        var token = JwtAuthHandler.CreateToken(signingKey, subject, expiresAt, role);
        httpContext.Response.Cookies.Append(JwtAuthHandler.CookieName, token, new CookieOptions
        {
            HttpOnly = true,
            Secure   = !env.IsDevelopment(),     // HTTPS-only in production
            SameSite = SameSiteMode.Lax,         // CSRF protection; works same-site (incl. different ports on localhost)
            Path     = "/",
            Expires  = expiresAt
        });
        return Results.Ok(new { role, username = subject, expiresAt = expiresAt.ToString("o") });
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
