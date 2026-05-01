using System.Text.Json;
using Beacon.Core.Security;
using Beacon.Core.Services;

namespace Beacon.Api;

public static class ApiKeyEndpoints
{
    private static readonly string[] KnownPermissions =
    [
        "_all", "consent:read", "consent:write", "tokens:write",
        "buckets:read", "buckets:write", "submissions:read", "submissions:write",
        "audit:read", "webhooks:read", "webhooks:write"
    ];

    public static void MapApiKeyEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/admin/api-keys", GetAllApiKeys)
            .RequireAuthorization("Admin")
            .ExcludeFromDescription();

        routes.MapPost("/api/admin/api-keys", CreateApiKey)
            .RequireAuthorization("Admin")
            .ExcludeFromDescription();

        routes.MapDelete("/api/admin/api-keys/{id:guid}", DeleteApiKey)
            .RequireAuthorization("Admin")
            .ExcludeFromDescription();

        routes.MapPatch("/api/admin/api-keys/{id:guid}/enabled", SetApiKeyEnabled)
            .RequireAuthorization("Admin")
            .ExcludeFromDescription();

        routes.MapPatch("/api/admin/api-keys/{id:guid}/permissions", UpdateApiKeyPermissions)
            .RequireAuthorization("Admin")
            .ExcludeFromDescription();

        routes.MapPatch("/api/admin/api-keys/{id:guid}/dates", UpdateApiKeyDates)
            .RequireAuthorization("Admin")
            .ExcludeFromDescription();

        routes.MapPost("/api/admin/api-keys/{id:guid}/rotate", RotateApiKey)
            .RequireAuthorization("Admin")
            .ExcludeFromDescription();
    }

    private static async Task<IResult> GetAllApiKeys(IApiKeyRepository repo)
    {
        var keys = await repo.GetAllAsync();
        return Results.Ok(keys.Select(k => new
        {
            k.Id,
            k.Name,
            Permissions = JsonSerializer.Deserialize<string[]>(k.Permissions) ?? [],
            k.IsEnabled,
            k.ActiveFrom,
            k.ActiveUntil,
            k.CreatedAt,
            k.LastUsedAt
        }));
    }

    private static async Task<IResult> CreateApiKey(CreateApiKeyRequest request, IApiKeyRepository repo)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 200)
            return Results.Json(new { error = "Name is required and must be under 200 characters." }, statusCode: 400);

        if (request.Permissions == null || request.Permissions.Length == 0)
            return Results.Json(new { error = "At least one permission is required." }, statusCode: 400);

        if (!ValidatePermissions(request.Permissions, out var permError))
            return Results.Json(new { error = permError }, statusCode: 400);

        if (request.ActiveFrom.HasValue && request.ActiveUntil.HasValue
            && request.ActiveUntil.Value <= request.ActiveFrom.Value)
            return Results.Json(new { error = "ActiveUntil must be after ActiveFrom." }, statusCode: 400);

        var existing = await repo.GetAllAsync();
        if (existing.Any(k => string.Equals(k.Name, request.Name!.Trim(), StringComparison.OrdinalIgnoreCase)))
            return Results.Json(new { error = "An API key with this name already exists." }, statusCode: 409);

        var (rawKey, keyHash) = ApiKeyGenerator.Generate();

        var key = await repo.CreateAsync(
            request.Name.Trim(),
            keyHash,
            request.Permissions,
            request.IsEnabled ?? true,
            request.ActiveFrom?.ToUniversalTime(),
            request.ActiveUntil?.ToUniversalTime());

        return Results.Ok(new
        {
            key.Id,
            key.Name,
            Permissions = request.Permissions,
            apiKey = rawKey  // shown once
        });
    }

    private static async Task<IResult> DeleteApiKey(Guid id, IApiKeyRepository repo)
    {
        var key = await repo.FindByIdAsync(id);
        if (key == null)
            return Results.Json(new { error = "API key not found." }, statusCode: 404);

        await repo.DeleteAsync(id);
        return Results.Ok(new { success = true });
    }

    private static async Task<IResult> SetApiKeyEnabled(Guid id, SetEnabledRequest request, IApiKeyRepository repo)
    {
        var key = await repo.FindByIdAsync(id);
        if (key == null)
            return Results.Json(new { error = "API key not found." }, statusCode: 404);

        await repo.UpdateEnabledAsync(id, request.IsEnabled);
        return Results.Ok(new { success = true });
    }

    private static async Task<IResult> UpdateApiKeyPermissions(Guid id, UpdatePermissionsRequest request, IApiKeyRepository repo)
    {
        var key = await repo.FindByIdAsync(id);
        if (key == null)
            return Results.Json(new { error = "API key not found." }, statusCode: 404);

        if (request.Permissions == null || request.Permissions.Length == 0)
            return Results.Json(new { error = "At least one permission is required." }, statusCode: 400);

        if (!ValidatePermissions(request.Permissions, out var permError))
            return Results.Json(new { error = permError }, statusCode: 400);

        await repo.UpdatePermissionsAsync(id, request.Permissions);
        return Results.Ok(new { success = true });
    }

    private static async Task<IResult> UpdateApiKeyDates(Guid id, UpdateDatesRequest request, IApiKeyRepository repo)
    {
        var key = await repo.FindByIdAsync(id);
        if (key == null)
            return Results.Json(new { error = "API key not found." }, statusCode: 404);

        if (request.ActiveFrom.HasValue && request.ActiveUntil.HasValue
            && request.ActiveUntil.Value <= request.ActiveFrom.Value)
            return Results.Json(new { error = "ActiveUntil must be after ActiveFrom." }, statusCode: 400);

        await repo.UpdateDatesAsync(id,
            request.ActiveFrom?.ToUniversalTime(),
            request.ActiveUntil?.ToUniversalTime());
        return Results.Ok(new { success = true });
    }

    private static async Task<IResult> RotateApiKey(Guid id, IApiKeyRepository repo)
    {
        var key = await repo.FindByIdAsync(id);
        if (key == null)
            return Results.Json(new { error = "API key not found." }, statusCode: 404);

        var (rawKey, keyHash) = ApiKeyGenerator.Generate();
        await repo.UpdateKeyHashAsync(id, keyHash);

        return Results.Ok(new { apiKey = rawKey }); // shown once
    }

    private static bool ValidatePermissions(string[] permissions, out string error)
    {
        error = "";
        foreach (var p in permissions)
        {
            if (!KnownPermissions.Contains(p))
            {
                error = $"Unknown permission: '{p}'. Valid values: {string.Join(", ", KnownPermissions)}.";
                return false;
            }
        }
        return true;
    }

    private record CreateApiKeyRequest(
        string? Name,
        string[]? Permissions,
        bool? IsEnabled,
        DateTime? ActiveFrom,
        DateTime? ActiveUntil);

    private record SetEnabledRequest(bool IsEnabled);
    private record UpdatePermissionsRequest(string[] Permissions);
    private record UpdateDatesRequest(DateTime? ActiveFrom, DateTime? ActiveUntil);
}
