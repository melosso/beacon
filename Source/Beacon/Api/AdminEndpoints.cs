using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Beacon.Core.Models;
using Beacon.Core.Security;
using Beacon.Core.Services;
using Beacon.Core.Validation;
using Beacon.Tokens;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Api;

public static class AdminEndpoints
{
    private const string ManagementTag = "Management";
    private const string IntegrationTag = "Integration";

    // The current list of supported languages as of right now
    private static readonly IReadOnlyList<string> SupportedLanguages = new List<string> { "en", "de", "fr", "nl", "pl", "es" }.AsReadOnly();

    public static void MapAdminEndpoints(this IEndpointRouteBuilder routes)
    {
        // Integration APIs (e.g. for external systems)
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

        routes.MapPost("/api/admin/buckets/{bucket}/override", BatchOverrideConsent)
            .RequireAuthorization()
            .ExcludeFromDescription();

        // Webhook Management APIs (excluded from OpenAPI)
        routes.MapGet("/api/admin/buckets/{bucket}/webhook", GetWebhookConfig)
            .RequireAuthorization()
            .ExcludeFromDescription();

        routes.MapPost("/api/admin/buckets/{bucket}/webhook", SaveWebhookConfig)
            .RequireAuthorization()
            .ExcludeFromDescription();

        routes.MapDelete("/api/admin/buckets/{bucket}/webhook", DeleteWebhookConfig)
            .RequireAuthorization()
            .ExcludeFromDescription();
    }

    private static async Task<IResult> OverrideConsent(
        [FromBody] OverrideConsentRequest request,
        [FromServices] IConsentService consentService,
        [FromServices] IConsentRepository consentRepository,
        [FromServices] IWebhookService webhookService,
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

        var emailHash = emailHasher.Hash(request.Email);
        logger.LogInformation(
            "Processing consent override: bucket={Bucket}, id={EmailId}, permission={Permission}, status={Status}, timestamp={Timestamp}",
            request.Bucket,
            emailHash[..12],
            request.Permission,
            status,
            DateTime.UtcNow);

        string? customFieldsJson = request.CustomFields is { Count: > 0 }
            ? JsonSerializer.Serialize(request.CustomFields)
            : null;

        try
        {
            await consentService.OverrideAsync(request.Bucket, request.Email, request.Permission, status, customFieldsJson);

            await TriggerWebhookSafe(webhookService, consentRepository, request.Bucket, request.Email, emailHash, customFieldsJson);

            return Results.Ok(new { message = "Consent updated" });
        }
        catch (DbUpdateException ex)
        {
            if (ex.InnerException?.Message?.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase) == true)
            {
                logger.LogWarning("Unique constraint violation during consent override for bucket={Bucket}, email={Email}, permission={Permission}: {ErrorMessage}", request.Bucket, request.Email, request.Permission, ex.InnerException?.Message);
                return Results.Conflict(new { error = "A record with the same email and permission already exists in this bucket." });
            }

            logger.LogError(ex, "Database update error during consent override for bucket={Bucket}, email={Email}, permission={Permission}", request.Bucket, request.Email, request.Permission);
            return Results.StatusCode(500); // Generic 500 for other database errors
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error occurred during consent override for bucket={Bucket}, email={Email}, permission={Permission}", request.Bucket, request.Email, request.Permission);
            return Results.StatusCode(500); // Generic 500 for other unexpected errors
        }
    }

    private static async Task<IResult> BatchOverrideConsent(
        string bucket,
        [FromBody] BatchOverrideRequest request,
        [FromServices] IConsentService consentService,
        [FromServices] IConsentRepository consentRepository,
        [FromServices] IWebhookService webhookService,
        [FromServices] EmailHasher emailHasher,
        ILogger<Program> logger)
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

        if (request.Permissions is null || request.Permissions.Count == 0)
        {
            return Results.BadRequest(new { error = "At least one permission is required" });
        }

        var normalizedBucket = bucket.Trim().ToLowerInvariant();
        var emailHash = emailHasher.Hash(request.Email);

        string? customFieldsJson = request.CustomFields is { Count: > 0 }
            ? JsonSerializer.Serialize(request.CustomFields)
            : null;

        try
        {
            foreach (var (permission, status) in request.Permissions)
            {
                if (!Enum.TryParse<ConsentStatus>(status, true, out var consentStatus))
                {
                    return Results.BadRequest(new { error = $"Invalid status '{status}' for permission '{permission}'" });
                }

                await consentService.OverrideAsync(normalizedBucket, request.Email, permission, consentStatus, customFieldsJson);
            }

            logger.LogInformation(
                "Batch consent override: bucket={Bucket}, id={EmailId}, permissions={Permissions}, timestamp={Timestamp}",
                normalizedBucket,
                emailHash[..12],
                string.Join(",", request.Permissions.Select(p => $"{p.Key}:{p.Value}")),
                DateTime.UtcNow);

            // Fire one webhook with full permission snapshot
            await TriggerWebhookSafe(webhookService, consentRepository, normalizedBucket, request.Email, emailHash, customFieldsJson);

            return Results.Ok(new { message = "Consent updated" });
        }
        catch (DbUpdateException ex)
        {
            if (ex.InnerException?.Message?.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase) == true)
            {
                return Results.Conflict(new { error = "A record with the same email and permission already exists in this bucket." });
            }

            logger.LogError(ex, "Database update error during batch consent override for bucket={Bucket}", normalizedBucket);
            return Results.StatusCode(500);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error during batch consent override for bucket={Bucket}", normalizedBucket);
            return Results.StatusCode(500);
        }
    }

    private static async Task<IResult> GenerateToken(
        [FromBody] GenerateTokenRequest request,
        [FromServices] TokenGenerator generator,
        [FromServices] IConsentService consentService,
        [FromServices] IConsentRepository consentRepository,
        [FromServices] IWebhookService webhookService,
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

        if (!string.IsNullOrEmpty(request.Language) && !SupportedLanguages.Contains(request.Language.ToLowerInvariant()))
        {
            return Results.BadRequest(new { error = $"Unsupported language code '{request.Language}'. Supported languages are: {string.Join(", ", SupportedLanguages)}" });
        }

        try
        {
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
            var hasChanges = false;
            foreach (var (permission, optedIn) in request.Permissions)
            {
                var status = optedIn ? ConsentStatus.OptedIn : ConsentStatus.OptedOut;

                if (request.SkipPermissionUpdate)
                {
                    // Only insert if record doesn't exist, preserving existing user preferences
                    var created = await consentService.EnsureAsync(request.Bucket, request.Email, permission, status, customFieldsJson);
                    if (created) hasChanges = true;
                }
                else
                {
                    // Always upsert (insert or update)
                    await consentService.OverrideAsync(request.Bucket, request.Email, permission, status, customFieldsJson);
                    hasChanges = true;
                }
            }

            var emailHash = emailHasher.Hash(request.Email);
            if (hasChanges)
            {
                await TriggerWebhookSafe(webhookService, consentRepository, request.Bucket, request.Email, emailHash, customFieldsJson);
            }

            var emailId = emailHash[..12];
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
        catch (DbUpdateException ex)
        {
            if (ex.InnerException?.Message?.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase) == true)
            {
                logger.LogWarning("Unique constraint violation during token generation for bucket={Bucket}, email={Email}: {ErrorMessage}", request.Bucket, request.Email, ex.InnerException?.Message);
                return Results.Conflict(new { error = "A record with the same email and permission already exists in this bucket." });
            }

            logger.LogError(ex, "Database update error during token generation for bucket={Bucket}, email={Email}", request.Bucket, request.Email);
            return Results.StatusCode(500);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error occurred during token generation for bucket={Bucket}, email={Email}", request.Bucket, request.Email);
            return Results.StatusCode(500);
        }
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
        var hash = emailHasher.Hash(normalizedEmail);

        var exists = await repository.EmailExistsInBucketAsync(normalizedBucket, hash);

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

    // Webhook endpoints

    private static async Task<IResult> GetWebhookConfig(
        string bucket,
        [FromServices] IWebhookService webhookService)
    {
        var bucketValidation = InputValidator.ValidateBucket(bucket);
        if (!bucketValidation.IsValid)
        {
            return Results.BadRequest(new { error = bucketValidation.Error });
        }

        var config = await webhookService.GetWebhookConfigAsync(bucket.Trim().ToLowerInvariant());
        if (config == null)
        {
            return Results.Ok(new { configured = false });
        }

        var headersDict = !string.IsNullOrEmpty(config.EncryptedHeaders)
            ? JsonSerializer.Deserialize<Dictionary<string, string>>(config.EncryptedHeaders)
            : new Dictionary<string, string>();

        return Results.Ok(new
        {
            configured = true,
            url = config.EncryptedUrl,
            method = config.EncryptedMethod,
            headers = headersDict,
            bodyTemplate = config.BodyTemplate,
            isEnabled = config.IsEnabled,
            lastTriggeredAt = config.LastTriggeredAt,
            triggerCount = config.TriggerCount
        });
    }

    private static async Task<IResult> SaveWebhookConfig(
        string bucket,
        [FromBody] WebhookConfigRequest request,
        [FromServices] IWebhookService webhookService)
    {
        var bucketValidation = InputValidator.ValidateBucket(bucket);
        if (!bucketValidation.IsValid)
        {
            return Results.BadRequest(new { error = bucketValidation.Error });
        }

        if (string.IsNullOrWhiteSpace(request.Url))
        {
            return Results.BadRequest(new { error = "URL is required" });
        }

        if (string.IsNullOrWhiteSpace(request.Method))
        {
            return Results.BadRequest(new { error = "HTTP method is required" });
        }

        var validMethods = new[] { "GET", "POST", "PUT", "PATCH", "DELETE" };
        if (!validMethods.Contains(request.Method.ToUpperInvariant()))
        {
            return Results.BadRequest(new { error = "Invalid HTTP method" });
        }

        // Validate URL format
        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            return Results.BadRequest(new { error = "Invalid URL format" });
        }

        // SSRF protection: block private/reserved IP addresses
        if (!await IsWebhookUrlSafeAsync(uri))
        {
            return Results.BadRequest(new { error = "URL must not point to a private or reserved address" });
        }

        var secret = await webhookService.SaveWebhookConfigAsync(
            bucket.Trim().ToLowerInvariant(),
            request.Url,
            request.Method,
            request.Headers,
            request.BodyTemplate);

        return Results.Ok(new
        {
            success = true,
            signingSecret = secret
        });
    }

    private static async Task<IResult> DeleteWebhookConfig(
        string bucket,
        [FromServices] IWebhookService webhookService)
    {
        var bucketValidation = InputValidator.ValidateBucket(bucket);
        if (!bucketValidation.IsValid)
        {
            return Results.BadRequest(new { error = bucketValidation.Error });
        }

        await webhookService.DeleteWebhookConfigAsync(bucket.Trim().ToLowerInvariant());
        return Results.Ok(new { success = true });
    }

    private static async Task<bool> IsWebhookUrlSafeAsync(Uri uri)
    {
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(uri.Host);
            return addresses.All(addr => !IsPrivateOrReserved(addr));
        }
        catch
        {
            return false;
        }
    }

    private static async Task TriggerWebhookSafe(
        IWebhookService webhookService,
        IConsentRepository repository,
        string bucket,
        string email,
        string emailHash,
        string? customFieldsJson)
    {
        try
        {
            var normalizedBucket = bucket.Trim().ToLowerInvariant();

            // Fetch full permission snapshot for this email
            var records = await repository.GetByEmailAsync(normalizedBucket, emailHash);
            var permissions = records.Select(r => new PermissionState
            {
                Permission = r.Permission,
                Status = r.Status
            }).ToList();

            if (permissions.Count == 0) return;

            var data = new WebhookTriggerData
            {
                Bucket = normalizedBucket,
                Email = email.Trim().ToLowerInvariant(),
                EmailHash = emailHash,
                Permissions = permissions,
                CustomFields = customFieldsJson
            };

            await webhookService.TriggerWebhookAsync(normalizedBucket, data);
        }
        catch
        {
            // Silently ignore webhook failures to not disrupt the main operation
        }
    }

    private static bool IsPrivateOrReserved(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
            return true;

        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] switch
            {
                10 => true,                                          // 10.0.0.0/8
                127 => true,                                         // 127.0.0.0/8
                169 when bytes[1] == 254 => true,                    // 169.254.0.0/16
                172 when bytes[1] >= 16 && bytes[1] <= 31 => true,   // 172.16.0.0/12
                192 when bytes[1] == 168 => true,                    // 192.168.0.0/16
                0 => true,                                           // 0.0.0.0/8
                100 when bytes[1] >= 64 && bytes[1] <= 127 => true,  // 100.64.0.0/10
                _ => false
            };
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal;
        }

        return false;
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

public sealed class BatchOverrideRequest
{
    public string Email { get; set; } = string.Empty;
    /// <summary>
    /// Permission states: {"newsletter": "OptedIn", "marketing": "OptedOut"}
    /// </summary>
    public Dictionary<string, string> Permissions { get; set; } = new();
    public Dictionary<string, string>? CustomFields { get; set; }
}

public sealed class WebhookConfigRequest
{
    public string Url { get; set; } = string.Empty;
    public string Method { get; set; } = "POST";
    public Dictionary<string, string>? Headers { get; set; }
    public string? BodyTemplate { get; set; }
}
