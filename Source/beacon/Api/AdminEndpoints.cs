using Beacon.Core.Models;
using Beacon.Core.Security;
using Beacon.Core.Services;
using Beacon.Core.Validation;
using Beacon.Tokens;
using Microsoft.AspNetCore.Mvc;

namespace Beacon.Api;

public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/api/consent/override", OverrideConsent)
            .WithName("OverrideConsent")
            .WithOpenApi()
            .RequireAuthorization()
            .WithDescription("Override consent status (admin only)");

        routes.MapPost("/api/tokens/generate", GenerateToken)
            .WithName("GenerateToken")
            .WithOpenApi()
            .RequireAuthorization()
            .WithDescription("Generate an opt-out token for an email");

        // Admin panel APIs
        routes.MapGet("/api/admin/buckets", GetBuckets)
            .WithName("GetBuckets")
            .WithOpenApi()
            .RequireAuthorization()
            .WithDescription("Get all buckets with summary stats");

        routes.MapGet("/api/admin/buckets/{bucket}", GetBucketDetails)
            .WithName("GetBucketDetails")
            .WithOpenApi()
            .RequireAuthorization()
            .WithDescription("Get detailed stats for a bucket");

        routes.MapGet("/api/admin/buckets/{bucket}/records", GetBucketRecords)
            .WithName("GetBucketRecords")
            .WithOpenApi()
            .RequireAuthorization()
            .WithDescription("Get paginated consent records for a bucket");

        routes.MapDelete("/api/admin/buckets/{bucket}", DeleteBucket)
            .WithName("DeleteBucket")
            .WithOpenApi()
            .RequireAuthorization()
            .WithDescription("Delete a bucket and all its consent records");

        routes.MapPost("/api/admin/buckets/{bucket}/check-email", CheckEmailExists)
            .WithName("CheckEmailExists")
            .WithOpenApi()
            .RequireAuthorization()
            .WithDescription("Check if an email already exists in a bucket");
    }

    private static async Task<IResult> OverrideConsent(
        [FromBody] OverrideConsentRequest request,
        [FromServices] IConsentService consentService,
        ILogger<Program> logger)
    {
        var bucketValidation = InputValidator.ValidateBucket(request.Bucket);
        if (!bucketValidation.IsValid)
        {
            return Results.BadRequest(new { error = bucketValidation.Error });
        }

        var emailValidation = InputValidator.ValidateEmail(request.Email);
        if (!emailValidation.IsValid)
        {
            return Results.BadRequest(new { error = emailValidation.Error });
        }

        var permissionValidation = InputValidator.ValidatePermission(request.Permission);
        if (!permissionValidation.IsValid)
        {
            return Results.BadRequest(new { error = permissionValidation.Error });
        }

        if (!Enum.TryParse<ConsentStatus>(request.Status, true, out var status))
        {
            return Results.BadRequest(new { error = "Invalid status. Use 'OptedIn' or 'OptedOut'" });
        }

        logger.LogInformation(
            "Admin consent override: bucket={Bucket}, permission={Permission}, status={Status}, timestamp={Timestamp}",
            request.Bucket,
            request.Permission,
            status,
            DateTime.UtcNow);

        await consentService.OverrideAsync(request.Bucket, request.Email, request.Permission, status);

        return Results.Ok(new { message = "Consent updated" });
    }

    private static async Task<IResult> GenerateToken(
        [FromBody] GenerateTokenRequest request,
        [FromServices] TokenGenerator generator,
        [FromServices] IConsentService consentService,
        ILogger<Program> logger)
    {
        var bucketValidation = InputValidator.ValidateBucket(request.Bucket);
        if (!bucketValidation.IsValid)
        {
            return Results.BadRequest(new { error = bucketValidation.Error });
        }

        var emailValidation = InputValidator.ValidateEmail(request.Email);
        if (!emailValidation.IsValid)
        {
            return Results.BadRequest(new { error = emailValidation.Error });
        }

        if (request.Permissions is null || request.Permissions.Count == 0)
        {
            return Results.BadRequest(new { error = "At least one permission is required" });
        }

        var permissionNames = request.Permissions.Keys.ToArray();

        var permissionsValidation = InputValidator.ValidatePermissions(permissionNames);
        if (!permissionsValidation.IsValid)
        {
            return Results.BadRequest(new { error = permissionsValidation.Error });
        }

        var tokenOptions = new Tokens.GenerateTokenRequest
        {
            AllowReplay = request.AllowReplay,
            ExpiryDays = request.ExpiryDays
        };

        var token = generator.Generate(request.Bucket, request.Email, permissionNames, tokenOptions);

        // Create/update consent records with specified states
        foreach (var (permission, optedIn) in request.Permissions)
        {
            var status = optedIn ? ConsentStatus.OptedIn : ConsentStatus.OptedOut;

            if (request.SkipPermissionUpdate)
            {
                // Only insert if record doesn't exist, preserving existing user preferences
                await consentService.EnsureAsync(request.Bucket, request.Email, permission, status);
            }
            else
            {
                // Always upsert (insert or update)
                await consentService.OverrideAsync(request.Bucket, request.Email, permission, status);
            }
        }

        logger.LogInformation(
            "Token generated: bucket={Bucket}, permissions={Permissions}, allowReplay={AllowReplay}, expiryDays={ExpiryDays}, skipUpdate={SkipUpdate}, timestamp={Timestamp}",
            request.Bucket,
            string.Join(",", request.Permissions.Select(p => $"{p.Key}:{(p.Value ? "in" : "out")}")),
            request.AllowReplay,
            request.ExpiryDays,
            request.SkipPermissionUpdate,
            DateTime.UtcNow);

        return Results.Ok(new GenerateTokenResponse { Token = token });
    }

    private static async Task<IResult> GetBuckets(
        [FromServices] IConsentRepository repository)
    {
        var buckets = await repository.GetBucketsAsync();
        return Results.Ok(buckets);
    }

    private static async Task<IResult> GetBucketDetails(
        string bucket,
        [FromServices] IConsentRepository repository)
    {
        var details = await repository.GetBucketDetailsAsync(bucket);
        return Results.Ok(details);
    }

    private static async Task<IResult> GetBucketRecords(
        string bucket,
        [FromQuery] int page,
        [FromQuery] int pageSize,
        [FromServices] IConsentRepository repository,
        [FromServices] Encryptor encryptor)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 10;
        if (pageSize > 100) pageSize = 100;

        var result = await repository.GetBucketRecordsAsync(bucket, page, pageSize);

        // Decrypt emails for admin display
        foreach (var record in result.Records)
        {
            if (!string.IsNullOrEmpty(record.EncryptedEmail))
            {
                try
                {
                    record.Email = encryptor.Decrypt(record.EncryptedEmail);
                }
                catch
                {
                    // Decryption failed, leave email as null
                }
            }
        }

        return Results.Ok(new
        {
            records = result.Records,
            total = result.Total,
            page = result.Page,
            pageSize = result.PageSize
        });
    }

    private static async Task<IResult> DeleteBucket(
        string bucket,
        [FromServices] IConsentRepository repository,
        ILogger<Program> logger)
    {
        if (string.IsNullOrWhiteSpace(bucket))
        {
            return Results.BadRequest(new { error = "Bucket name is required" });
        }

        logger.LogInformation(
            "Bucket deletion: bucket={Bucket}, timestamp={Timestamp}",
            bucket,
            DateTime.UtcNow);

        var deleted = await repository.DeleteBucketAsync(bucket);

        return Results.Ok(new { message = "Bucket deleted", recordsDeleted = deleted });
    }

    private static async Task<IResult> CheckEmailExists(
        string bucket,
        [FromBody] CheckEmailRequest request,
        [FromServices] IConsentRepository repository,
        [FromServices] EmailHasher emailHasher)
    {
        if (string.IsNullOrWhiteSpace(bucket))
        {
            return Results.BadRequest(new { error = "Bucket name is required" });
        }

        var emailValidation = InputValidator.ValidateEmail(request.Email);
        if (!emailValidation.IsValid)
        {
            return Results.BadRequest(new { error = emailValidation.Error });
        }

        var normalizedBucket = bucket.Trim().ToLowerInvariant();
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var emailHash = emailHasher.Hash(normalizedEmail);

        var exists = await repository.EmailExistsInBucketAsync(normalizedBucket, emailHash);

        return Results.Ok(new { exists });
    }
}

public sealed class OverrideConsentRequest
{
    public string Bucket { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Permission { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public sealed class GenerateTokenRequest
{
    public string Bucket { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Permission states: {"newsletter": true, "marketing": false}
    /// true = OptedIn, false = OptedOut
    /// </summary>
    public Dictionary<string, bool>? Permissions { get; set; }

    /// <summary>
    /// Allow the token to be reused multiple times until expiry.
    /// Default: true (can revisit preference page multiple times).
    /// </summary>
    public bool AllowReplay { get; set; } = true;

    /// <summary>
    /// Token expiry in days from generation.
    /// Default: 60 days.
    /// </summary>
    public int ExpiryDays { get; set; } = 60;

    /// <summary>
    /// When true, only creates consent records if they don't exist.
    /// Existing records are preserved, preventing ERP data from overwriting user preferences.
    /// Default: false (always upsert).
    /// </summary>
    public bool SkipPermissionUpdate { get; set; } = false;
}

public sealed class GenerateTokenResponse
{
    public string Token { get; set; } = string.Empty;
}

public sealed class CheckEmailRequest
{
    public string Email { get; set; } = string.Empty;
}
