using System.Security.Claims;
using Beacon.Core.Security;
using Beacon.Core.Services;

namespace Beacon.Api;

public static class UserEndpoints
{
    private static readonly string[] ValidRoles = ["admin", "user"];

    public static void MapUserEndpoints(this IEndpointRouteBuilder routes)
    {
        // Admin-only: list all users
        routes.MapGet("/api/admin/users", GetAllUsers)
            .RequireAuthorization("Admin")
            .ExcludeFromDescription();

        // Admin-only: create user
        routes.MapPost("/api/admin/users", CreateUser)
            .RequireAuthorization("Admin")
            .ExcludeFromDescription();

        // Admin-only: delete user
        routes.MapDelete("/api/admin/users/{id:guid}", DeleteUser)
            .RequireAuthorization("Admin")
            .ExcludeFromDescription();

        // Admin-only: change role
        routes.MapPatch("/api/admin/users/{id:guid}/role", ChangeRole)
            .RequireAuthorization("Admin")
            .ExcludeFromDescription();

        // Admin-only: rename user
        routes.MapPatch("/api/admin/users/{id:guid}/username", ChangeUsername)
            .RequireAuthorization("Admin")
            .ExcludeFromDescription();

        // Admin can reset any; user can reset own
        routes.MapPost("/api/admin/users/{id:guid}/api-key", RegenerateApiKey)
            .RequireAuthorization()
            .ExcludeFromDescription();

        // Admin can reset any; user must provide currentPassword for own
        routes.MapPatch("/api/admin/users/{id:guid}/password", ChangePassword)
            .RequireAuthorization()
            .ExcludeFromDescription();

        // Any authenticated user: own profile
        routes.MapGet("/api/admin/users/me", GetMe)
            .RequireAuthorization()
            .ExcludeFromDescription();
    }

    private static async Task<IResult> GetAllUsers(IUserRepository repo)
    {
        var users = await repo.GetAllAsync();
        return Results.Ok(users.Select(u => new
        {
            u.Id,
            u.Username,
            u.Role,
            u.IsEnabled,
            u.CreatedAt,
            u.LastLoginAt
        }));
    }

    private static async Task<IResult> CreateUser(CreateUserRequest request, IUserRepository repo)
    {
        if (!ValidateUsername(request.Username, out var usernameError))
            return Results.Json(new { error = usernameError }, statusCode: 400);

        if (!ValidatePassword(request.Password, out var passwordError))
            return Results.Json(new { error = passwordError }, statusCode: 400);

        var role = request.Role?.ToLowerInvariant() ?? "user";
        if (!ValidRoles.Contains(role))
            return Results.Json(new { error = "Role must be 'admin' or 'user'." }, statusCode: 400);

        var existing = await repo.FindByUsernameAsync(request.Username!);
        if (existing != null)
            return Results.Json(new { error = "Username already exists." }, statusCode: 409);

        var (hash, salt) = PasswordHasher.HashPassword(request.Password!);
        var (apiKey, apiKeyHash) = ApiKeyGenerator.Generate();

        var user = await repo.CreateAsync(request.Username!, hash, salt, role, apiKeyHash);

        return Results.Ok(new
        {
            user.Id,
            user.Username,
            user.Role,
            apiKey  // shown once
        });
    }

    private static async Task<IResult> DeleteUser(Guid id, HttpContext ctx, IUserRepository repo)
    {
        var currentUsername = ctx.User.FindFirstValue(ClaimTypes.Name);
        var user = await repo.FindByIdAsync(id);
        if (user == null)
            return Results.Json(new { error = "User not found." }, statusCode: 404);

        if (string.Equals(user.Username, currentUsername, StringComparison.OrdinalIgnoreCase))
            return Results.Json(new { error = "Cannot delete your own account." }, statusCode: 400);

        await repo.DeleteAsync(id);
        return Results.Ok(new { success = true });
    }

    private static async Task<IResult> ChangeRole(Guid id, ChangeRoleRequest request, HttpContext ctx, IUserRepository repo)
    {
        var role = request.Role?.ToLowerInvariant() ?? "";
        if (!ValidRoles.Contains(role))
            return Results.Json(new { error = "Role must be 'admin' or 'user'." }, statusCode: 400);

        var currentUsername = ctx.User.FindFirstValue(ClaimTypes.Name);
        var user = await repo.FindByIdAsync(id);
        if (user == null)
            return Results.Json(new { error = "User not found." }, statusCode: 404);

        if (string.Equals(user.Username, currentUsername, StringComparison.OrdinalIgnoreCase) && role != "admin")
            return Results.Json(new { error = "Cannot demote your own account." }, statusCode: 400);

        await repo.UpdateRoleAsync(id, role);
        return Results.Ok(new { success = true });
    }

    private static async Task<IResult> RegenerateApiKey(Guid id, HttpContext ctx, IUserRepository repo)
    {
        var currentUsername = ctx.User.FindFirstValue(ClaimTypes.Name);
        var currentRole = ctx.User.FindFirstValue(ClaimTypes.Role) ?? "user";

        var user = await repo.FindByIdAsync(id);
        if (user == null)
            return Results.Json(new { error = "User not found." }, statusCode: 404);

        // Users can only regenerate their own key; admins can regenerate any
        if (currentRole != "admin" &&
            !string.Equals(user.Username, currentUsername, StringComparison.OrdinalIgnoreCase))
        {
            return Results.Json(new { error = "Forbidden." }, statusCode: 403);
        }

        var (apiKey, apiKeyHash) = ApiKeyGenerator.Generate();
        await repo.UpdateApiKeyAsync(id, apiKeyHash);

        return Results.Ok(new { apiKey }); // shown once
    }

    private static async Task<IResult> ChangePassword(Guid id, ChangePasswordRequest request, HttpContext ctx, IUserRepository repo)
    {
        var currentUsername = ctx.User.FindFirstValue(ClaimTypes.Name);
        var currentRole = ctx.User.FindFirstValue(ClaimTypes.Role) ?? "user";

        var user = await repo.FindByIdAsync(id);
        if (user == null)
            return Results.Json(new { error = "User not found." }, statusCode: 404);

        var isSelf = string.Equals(user.Username, currentUsername, StringComparison.OrdinalIgnoreCase);

        if (!isSelf && currentRole != "admin")
            return Results.Json(new { error = "Forbidden." }, statusCode: 403);

        // All users must provide their current password when changing their own
        if (isSelf)
        {
            if (string.IsNullOrEmpty(request.CurrentPassword))
                return Results.Json(new { error = "Current password is required." }, statusCode: 400);

            if (!PasswordHasher.VerifyPassword(request.CurrentPassword, user.PasswordHash, user.Salt))
                return Results.Json(new { error = "Current password is incorrect." }, statusCode: 401);
        }

        if (!ValidatePassword(request.NewPassword, out var passwordError))
            return Results.Json(new { error = passwordError }, statusCode: 400);

        var (newHash, newSalt) = PasswordHasher.HashPassword(request.NewPassword!);
        await repo.UpdatePasswordAsync(id, newHash, newSalt);

        return Results.Ok(new { success = true });
    }

    private static async Task<IResult> GetMe(HttpContext ctx, IUserRepository repo)
    {
        var username = ctx.User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(username))
            return Results.Json(new { error = "Unauthorized." }, statusCode: 401);

        var user = await repo.FindByUsernameAsync(username);
        if (user == null)
        {
            // Global API key / legacy mode — return minimal profile
            var role = ctx.User.FindFirstValue(ClaimTypes.Role) ?? "admin";
            return Results.Ok(new { id = (Guid?)null, username, role, lastLoginAt = (DateTime?)null });
        }

        return Results.Ok(new
        {
            id = (Guid?)user.Id,
            user.Username,
            user.Role,
            user.LastLoginAt
        });
    }

    private static bool ValidateUsername(string? username, out string error)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(username))
        {
            error = "Username is required.";
            return false;
        }
        if (username.Length < 3 || username.Length > 100)
        {
            error = "Username must be 3-100 characters.";
            return false;
        }
        if (!System.Text.RegularExpressions.Regex.IsMatch(username, @"^[a-zA-Z0-9\-_\.@]+$"))
        {
            error = "Username may only contain letters, numbers, hyphens, underscores, dots, and @ signs.";
            return false;
        }
        return true;
    }

    private static bool ValidatePassword(string? password, out string error)
    {
        error = "";
        if (string.IsNullOrEmpty(password))
        {
            error = "Password is required.";
            return false;
        }
        if (password.Length < 12)
        {
            error = "Password must be at least 12 characters.";
            return false;
        }
        return true;
    }

    private static async Task<IResult> ChangeUsername(Guid id, ChangeUsernameRequest request, IUserRepository repo)
    {
        if (!ValidateUsername(request.Username, out var usernameError))
            return Results.Json(new { error = usernameError }, statusCode: 400);

        var user = await repo.FindByIdAsync(id);
        if (user == null)
            return Results.Json(new { error = "User not found." }, statusCode: 404);

        var existing = await repo.FindByUsernameAsync(request.Username!);
        if (existing != null && existing.Id != id)
            return Results.Json(new { error = "Username already exists." }, statusCode: 409);

        await repo.UpdateUsernameAsync(id, request.Username!);
        return Results.Ok(new { success = true });
    }

    private record CreateUserRequest(string? Username, string? Password, string? Role);
    private record ChangeRoleRequest(string? Role);
    private record ChangeUsernameRequest(string? Username);
    private record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);
}
