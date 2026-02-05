using System.Text.Json;
using Beacon.Core.Models;
using Beacon.Core.Security;
using Beacon.Core.Services;
using Beacon.Core.Validation;
using Beacon.Tokens;
using Microsoft.AspNetCore.Mvc;

namespace Beacon.Api;

public static class AdminEndpoints
{
    private const string ManagementTag = "Management";
    private const string IntegrationTag = "Integration";

    public static void MapAdminEndpoints(this IEndpointRouteBuilder routes)
    {
        // Integration APIs (for external systems)
        routes.MapPost("/api/consent/override", OverrideConsent)
            .WithName("OverrideConsent")
            .WithTags(IntegrationTag)
            .WithOpenApi()
            .RequireAuthorization()
            .WithDescription("Override consent status for an email. Use to sync consent state from external systems.");

        routes.MapPost("/api/tokens/generate", GenerateToken)
            .WithName("GenerateToken")
            .WithTags(IntegrationTag)
            .WithOpenApi()
            .RequireAuthorization()
            .WithDescription("Generate a preference management token for an email. Returns a URL-safe token for the /u/{token} endpoint.");

        routes.MapGet("/api/bucket/{bucket}/records", GetAllBucketRecords)
            .WithName("GetAllBucketRecords")
            .WithTags(IntegrationTag)
            .WithOpenApi()
            .RequireAuthorization()
            .WithDescription("Retrieve all consent records for a bucket. Returns decrypted emails and permission states.");

        // Management APIs (admin panel)
        routes.MapGet("/api/admin/buckets", GetBuckets)
            .WithName("GetBuckets")
            .WithTags(ManagementTag)
            .WithOpenApi()
            .RequireAuthorization()
            .WithDescription("List all buckets with email counts and available permissions.");

        routes.MapGet("/api/admin/buckets/{bucket}", GetBucketDetails)
            .WithName("GetBucketDetails")
            .WithTags(ManagementTag)
            .WithOpenApi()
            .RequireAuthorization()
            .WithDescription("Get statistics for a bucket including opt-in/opt-out counts per permission.");

        routes.MapGet("/api/admin/buckets/{bucket}/records", GetBucketRecords)
            .WithName("GetBucketRecords")
            .WithTags(ManagementTag)
            .WithOpenApi()
            .RequireAuthorization()
            .WithDescription("Get paginated consent records for a bucket with sorting and search support.");

        routes.MapDelete("/api/admin/buckets/{bucket}", DeleteBucket)
            .WithName("DeleteBucket")
            .WithTags(ManagementTag)
            .WithOpenApi()
            .RequireAuthorization()
            .WithDescription("Delete a bucket and all its consent records. This action is irreversible.");

        routes.MapDelete("/api/admin/buckets/{bucket}/records/{emailHash}", DeleteRecord)
            .WithName("DeleteRecord")
            .WithTags(ManagementTag)
            .WithOpenApi()
            .RequireAuthorization()
            .WithDescription("Delete all consent records for a specific email hash within a bucket.");

        routes.MapPost("/api/admin/buckets/{bucket}/check-email", CheckEmailExists)
            .WithName("CheckEmailExists")
            .WithTags(ManagementTag)
            .WithOpenApi()
            .RequireAuthorization()
            .WithDescription("Check if an email address exists in a bucket.");
    }

    private static async Task<IResult> OverrideConsent(
        [FromBody] OverrideConsentRequest request,
        [FromServices] IConsentService consentService,
        [FromServices] EmailHasher emailHasher,
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

        var emailId = emailHasher.Hash(request.Email)[..12];
        logger.LogInformation(
            "Processing consent override: bucket={Bucket}, id={EmailId}, permission={Permission}, status={Status}, timestamp={Timestamp}",
            request.Bucket,
            emailId,
            request.Permission,
            status,
            DateTime.UtcNow);

        string? customFieldsJson = request.CustomFields is { Count: > 0 }
            ? JsonSerializer.Serialize(request.CustomFields)
            : null;

        await consentService.OverrideAsync(request.Bucket, request.Email, request.Permission, status, customFieldsJson);

        return Results.Ok(new { message = "Consent updated" });
    }

    private static async Task<IResult> GenerateToken(
        [FromBody] GenerateTokenRequest request,
        [FromServices] TokenGenerator generator,
        [FromServices] IConsentService consentService,
        [FromServices] EmailHasher emailHasher,
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
            ExpiryDays = request.ExpiryDays,
            Language = request.Language
        };

        var token = generator.Generate(request.Bucket, request.Email, permissionNames, tokenOptions);

        // Serialize custom fields to JSON for storage
        string? customFieldsJson = request.CustomFields is { Count: > 0 }
            ? JsonSerializer.Serialize(request.CustomFields)
            : null;

        // Create/update consent records with specified states
        foreach (var (permission, optedIn) in request.Permissions)
        {
            var status = optedIn ? ConsentStatus.OptedIn : ConsentStatus.OptedOut;

            if (request.SkipPermissionUpdate)
            {
                // Only insert if record doesn't exist, preserving existing user preferences
                await consentService.EnsureAsync(request.Bucket, request.Email, permission, status, customFieldsJson);
            }
            else
            {
                // Always upsert (insert or update)
                await consentService.OverrideAsync(request.Bucket, request.Email, permission, status, customFieldsJson);
            }
        }

        var emailId = emailHasher.Hash(request.Email)[..12];
        logger.LogInformation(
            "Token generated: bucket={Bucket}, id={EmailId}, permissions={Permissions}, allowReplay={AllowReplay}, expiryDays={ExpiryDays}, skipUpdate={SkipUpdate}, timestamp={Timestamp}",
            request.Bucket,
            emailId,
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
        var bucketValidation = InputValidator.ValidateBucket(bucket);
        if (!bucketValidation.IsValid)
        {
            return Results.BadRequest(new { error = bucketValidation.Error });
        }

        var normalizedBucket = bucket.Trim().ToLowerInvariant();
        var details = await repository.GetBucketDetailsAsync(normalizedBucket);
        return Results.Ok(details);
    }

    private static async Task<IResult> GetBucketRecords(
        string bucket,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortDir = null,
        [FromQuery] string? search = null,
        [FromQuery] string? searchType = null,
        [FromServices] IConsentRepository? repository = null,
        [FromServices] Encryptor? encryptor = null)
    {
        var bucketValidation = InputValidator.ValidateBucket(bucket);
        if (!bucketValidation.IsValid)
        {
            return Results.BadRequest(new { error = bucketValidation.Error });
        }

        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 10;
        if (pageSize > 100) pageSize = 100;

        var normalizedBucket = bucket.Trim().ToLowerInvariant();

        // For ID search, pass to repository. For email search, we filter after decryption.
        var idSearch = searchType == "email" ? null : search;
        var result = await repository!.GetBucketRecordsAsync(normalizedBucket, page, pageSize, sortBy, sortDir, idSearch);

        // Decrypt emails for admin display
        foreach (var record in result.Records)
        {
            if (!string.IsNullOrEmpty(record.EncryptedEmail))
            {
                try
                {
                    record.Email = encryptor!.Decrypt(record.EncryptedEmail);
                }
                catch
                {
                    // Decryption failed, leave email as null
                }
            }
        }

        // If searching by email, filter after decryption
        var records = result.Records;
        var total = result.Total;
        if (searchType == "email" && !string.IsNullOrWhiteSpace(search))
        {
            var searchLower = search.ToLowerInvariant();
            records = records.Where(r =>
                r.Email != null && r.Email.ToLowerInvariant().Contains(searchLower)
            ).ToList();
            total = records.Count;
        }

        return Results.Ok(new
        {
            records,
            total,
            page = result.Page,
            pageSize = result.PageSize
        });
    }

    private static async Task<IResult> DeleteBucket(
        string bucket,
        [FromServices] IConsentRepository repository,
        ILogger<Program> logger)
    {
        var bucketValidation = InputValidator.ValidateBucket(bucket);
        if (!bucketValidation.IsValid)
        {
            return Results.BadRequest(new { error = bucketValidation.Error });
        }

        var normalizedBucket = bucket.Trim().ToLowerInvariant();

        logger.LogInformation(
            "Bucket deletion: bucket={Bucket}, timestamp={Timestamp}",
            normalizedBucket,
            DateTime.UtcNow);

        var deleted = await repository.DeleteBucketAsync(normalizedBucket);

        return Results.Ok(new { message = "Bucket deleted", recordsDeleted = deleted });
    }

    private static async Task<IResult> DeleteRecord(
        string bucket,
        string emailHash,
        [FromServices] IConsentRepository repository,
        ILogger<Program> logger)
    {
        var bucketValidation = InputValidator.ValidateBucket(bucket);
        if (!bucketValidation.IsValid)
        {
            return Results.BadRequest(new { error = bucketValidation.Error });
        }

        if (string.IsNullOrWhiteSpace(emailHash))
        {
            return Results.BadRequest(new { error = "Email hash is required" });
        }

        var normalizedBucket = bucket.Trim().ToLowerInvariant();

        logger.LogInformation(
            "Record deletion: bucket={Bucket}, id={EmailId}, timestamp={Timestamp}",
            normalizedBucket,
            emailHash[..Math.Min(12, emailHash.Length)],
            DateTime.UtcNow);

        var deleted = await repository.DeleteRecordAsync(normalizedBucket, emailHash);

        return Results.Ok(new { message = "Record deleted", permissionsDeleted = deleted });
    }

    private static async Task<IResult> CheckEmailExists(
        string bucket,
        [FromBody] CheckEmailRequest request,
        [FromServices] IConsentRepository repository,
        [FromServices] EmailHasher emailHasher)
    {
        var bucketValidation = InputValidator.ValidateBucket(bucket);
        if (!bucketValidation.IsValid)
        {
            return Results.BadRequest(new { error = bucketValidation.Error });
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

    private static async Task<IResult> GetAllBucketRecords(
        string bucket,
        [FromServices] IConsentRepository repository,
        [FromServices] Encryptor encryptor)
    {
        var bucketValidation = InputValidator.ValidateBucket(bucket);
        if (!bucketValidation.IsValid)
        {
            return Results.BadRequest(new { error = bucketValidation.Error });
        }

        var normalizedBucket = bucket.Trim().ToLowerInvariant();
        var records = await repository.GetAllBucketRecordsAsync(normalizedBucket);

        // Decrypt emails for display
        foreach (var record in records)
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

        return Results.Ok(new { bucket = normalizedBucket, records, total = records.Count });
    }
}

public sealed class OverrideConsentRequest
{
    public string Bucket { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Permission { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public Dictionary<string, string>? CustomFields { get; set; }
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

    /// <summary>
    /// Language code for the preference page.
    /// Supported: "en", "de", "fr", "nl", "pl", "es".
    /// Default: "en" (English).
    /// </summary>
    public string Language { get; set; } = "en";

    /// <summary>
    /// Optional custom fields to store alongside the email record.
    /// These are returned when fetching bucket data via the API.
    /// Example: {"company": "Acme", "source": "webinar"}
    /// </summary>
    public Dictionary<string, string>? CustomFields { get; set; }
}

public sealed class GenerateTokenResponse
{
    public string Token { get; set; } = string.Empty;
}

public sealed class CheckEmailRequest
{
    public string Email { get; set; } = string.Empty;
}
