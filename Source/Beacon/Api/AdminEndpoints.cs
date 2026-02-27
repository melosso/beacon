using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
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
    private const string PermissionTag = "Permission Management";

    // The current list of supported languages as of right now
    private static readonly IReadOnlyList<string> SupportedLanguages = new List<string> { "en", "de", "fr", "nl", "pl", "es" }.AsReadOnly();

    public static void MapAdminEndpoints(this IEndpointRouteBuilder routes)
    {
        // Integration APIs (e.g. for external systems)
        routes.MapPost("/api/consent/override", OverrideConsent)
            .WithName("OverrideConsent")
            .WithTags(PermissionTag)
            .RequireAuthorization()
            .WithDescription("Override consent status for an email. Use to sync consent state from external systems.");

        routes.MapPost("/api/tokens/generate", GenerateToken)
            .WithName("GenerateToken")
            .WithTags(PermissionTag)
            .RequireAuthorization()
            .WithDescription("Generate a preference management token for an email. Returns a URL-safe token for the /u/{token} endpoint.");

        routes.MapGet("/api/bucket/{bucket}/records", GetAllBucketRecords)
            .WithName("GetAllBucketRecords")
            .WithTags(PermissionTag)
            .RequireAuthorization()
            .WithDescription("Retrieve all consent records for a bucket. Returns decrypted emails and permission states.");

        // Management APIs (excluded from public OpenAPI docs)
        routes.MapGet("/api/admin/buckets", GetBuckets)
            .RequireAuthorization()
            .ExcludeFromDescription();

        routes.MapGet("/api/admin/buckets/{bucket}", GetBucketDetails)
            .RequireAuthorization()
            .ExcludeFromDescription();

        routes.MapGet("/api/admin/buckets/{bucket}/records", GetBucketRecords)
            .RequireAuthorization()
            .ExcludeFromDescription();

        routes.MapPost("/api/admin/buckets/{bucket}/archive", ArchiveBucket)
            .RequireAuthorization()
            .ExcludeFromDescription();

        routes.MapPost("/api/admin/buckets/{bucket}/unarchive", UnarchiveBucket)
            .RequireAuthorization()
            .ExcludeFromDescription();

        routes.MapPost("/api/admin/buckets/{bucket}/permissions", AddBucketPermission)
            .RequireAuthorization()
            .ExcludeFromDescription();

        routes.MapDelete("/api/admin/buckets/{bucket}", DeleteBucket)
            .RequireAuthorization()
            .ExcludeFromDescription();

        routes.MapDelete("/api/admin/buckets/{bucket}/records/{emailHash}", DeleteRecord)
            .RequireAuthorization()
            .ExcludeFromDescription();

        routes.MapDelete("/api/admin/buckets/{bucket}/permissions/{permission}", DeletePermission)
            .RequireAuthorization()
            .ExcludeFromDescription();

        routes.MapPost("/api/admin/buckets/{bucket}/check-email", CheckEmailExists)
            .RequireAuthorization()
            .ExcludeFromDescription();

        routes.MapGet("/api/admin/identities", GetIdentities)
            .RequireAuthorization()
            .ExcludeFromDescription();

        routes.MapGet("/api/admin/identities/{emailHash}", GetIdentityDetails)
            .RequireAuthorization()
            .ExcludeFromDescription();

        routes.MapPost("/api/admin/buckets/{bucket}/override", BatchOverrideConsent)
            .RequireAuthorization()
            .ExcludeFromDescription();

        routes.MapGet("/api/admin/webhooks/buckets", GetWebhookBuckets)
            .RequireAuthorization()
            .ExcludeFromDescription();

        routes.MapGet("/api/admin/buckets/{bucket}/webhook", GetWebhookConfig)
            .RequireAuthorization()
            .ExcludeFromDescription();

        routes.MapPost("/api/admin/buckets/{bucket}/webhook", SaveWebhookConfig)
            .RequireAuthorization()
            .ExcludeFromDescription();

        routes.MapDelete("/api/admin/buckets/{bucket}/webhook", DeleteWebhookConfig)
            .RequireAuthorization()
            .ExcludeFromDescription();

        routes.MapGet("/api/admin/buckets/{bucket}/webhook/errors", GetWebhookErrors)
            .RequireAuthorization()
            .ExcludeFromDescription();

        routes.MapDelete("/api/admin/buckets/{bucket}/webhook/errors/{id}", DeleteWebhookError)
            .RequireAuthorization()
            .ExcludeFromDescription();

        routes.MapDelete("/api/admin/buckets/{bucket}/webhook/errors", ClearWebhookErrors)
            .RequireAuthorization()
            .ExcludeFromDescription();

        routes.MapGet("/api/admin/events", StreamEvents)
            .RequireAuthorization()
            .ExcludeFromDescription();

        routes.MapGet("/api/admin/settings", GetSystemConfiguration)
            .RequireAuthorization()
            .ExcludeFromDescription();

        routes.MapPut("/api/admin/settings", SaveSystemConfiguration)
            .RequireAuthorization()
            .ExcludeFromDescription();

        routes.MapGet("/api/admin/buckets/{bucket}/options", GetBucketOptions)
            .RequireAuthorization()
            .ExcludeFromDescription();

        routes.MapPut("/api/admin/buckets/{bucket}/options", SaveBucketOptions)
            .RequireAuthorization()
            .ExcludeFromDescription();
    }

    private static async Task<IResult> OverrideConsent(
        [FromBody] OverrideConsentRequest request,
        [FromServices] IConsentService consentService,
        [FromServices] IConsentRepository consentRepository,
        [FromServices] IWebhookService webhookService,
        [FromServices] IBucketRepository bucketRepository,
        [FromServices] EmailHasher emailHasher,
        [FromServices] IAdminNotificationService notifications,
        ILogger<Program> logger)
    {
        var bucketValidation = InputValidator.ValidateBucket(request.Bucket);
        if (!bucketValidation.IsValid)
        {
            return Results.BadRequest(new { error = bucketValidation.Error });
        }

        if (await bucketRepository.IsArchivedAsync(request.Bucket.Trim().ToLowerInvariant()))
        {
            return Results.Conflict(new { error = "Bucket is archived" });
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

        if (!Enum.TryParse<ConsentStatus>(request.Status, true, out var status) || status == ConsentStatus.PendingConfirmation)
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
            await notifications.PublishConsentUpdateAsync(new ConsentUpdateNotification(request.Bucket));

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
        [FromServices] IBucketRepository bucketRepository,
        [FromServices] EmailHasher emailHasher,
        [FromServices] IAdminNotificationService notifications,
        ILogger<Program> logger)
    {
        var bucketValidation = InputValidator.ValidateBucket(bucket);
        if (!bucketValidation.IsValid)
        {
            return Results.BadRequest(new { error = bucketValidation.Error });
        }

        if (await bucketRepository.IsArchivedAsync(bucket.Trim().ToLowerInvariant()))
        {
            return Results.Conflict(new { error = "Bucket is archived" });
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
            using var transaction = await consentService.BeginTransactionAsync();

            foreach (var (permission, status) in request.Permissions)
            {
                if (!Enum.TryParse<ConsentStatus>(status, true, out var consentStatus) || consentStatus == ConsentStatus.PendingConfirmation)
                {
                    return Results.BadRequest(new { error = $"Invalid status '{status}' for permission '{permission}'" });
                }

                await consentService.OverrideAsync(normalizedBucket, request.Email, permission, consentStatus, customFieldsJson);
            }

            await consentService.CommitTransactionAsync();

            logger.LogInformation(
                "Batch consent override: bucket={Bucket}, id={EmailId}, permissions={Permissions}, timestamp={Timestamp}",
                normalizedBucket,
                emailHash[..12],
                string.Join(",", request.Permissions.Select(p => $"{p.Key}:{p.Value}")),
                DateTime.UtcNow);

            // Fire one webhook with full permission snapshot
            await TriggerWebhookSafe(webhookService, consentRepository, normalizedBucket, request.Email, emailHash, customFieldsJson);
            await notifications.PublishConsentUpdateAsync(new ConsentUpdateNotification(normalizedBucket));

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
        HttpContext context,
        [FromBody] GenerateTokenRequest request,
        [FromServices] TokenGenerator generator,
        [FromServices] IConsentService consentService,
        [FromServices] IConsentRepository consentRepository,
        [FromServices] IWebhookService webhookService,
        [FromServices] IBucketRepository bucketRepository,
        [FromServices] EmailHasher emailHasher,
        [FromServices] IAdminNotificationService notifications,
        [FromServices] ISystemConfigurationService configService,
        [FromServices] IEmailQueueRepository emailQueueRepo,
        [FromServices] IBucketOptionsRepository bucketOptionsRepo,
        [FromServices] Encryptor encryptor,
        [FromServices] Beacon.Core.Services.InstanceOptions instanceOptions,
        ILogger<Program> logger)
    {
        var bucketValidation = InputValidator.ValidateBucket(request.Bucket);
        if (!bucketValidation.IsValid)
        {
            return Results.BadRequest(new { error = bucketValidation.Error });
        }

        if (await bucketRepository.IsArchivedAsync(request.Bucket.Trim().ToLowerInvariant()))
        {
            return Results.Conflict(new { error = "Bucket is archived" });
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

        var config = configService.Get();
        var doubleOptInActive = !instanceOptions.DisableEmailNotifications
            && config.EnableDoubleOptIn
            && config.EmailNotifications
            && config.EmailProvider != "none";

        var normalizedBucket = request.Bucket.Trim().ToLowerInvariant();
        var emailHash = emailHasher.Hash(request.Email);

        if (doubleOptInActive)
        {
            var bucketOpts = await bucketOptionsRepo.GetAsync(normalizedBucket);
            doubleOptInActive = bucketOpts.DoubleOptIn;
        }

        try
        {
            using var transaction = await consentService.BeginTransactionAsync();

            var tokenOptions = new Tokens.GenerateTokenRequest
            {
                AllowReplay = request.AllowReplay,
                ExpiryDays = request.ExpiryDays,
                Language = string.IsNullOrEmpty(request.Language)
                    ? config.DefaultLanguage
                    : request.Language
            };

            var token = generator.Generate(request.Bucket, request.Email, permissionNames, tokenOptions);

            // Serialize custom fields to JSON for storage
            string? customFieldsJson = request.CustomFields is { Count: > 0 }
                ? JsonSerializer.Serialize(request.CustomFields)
                : null;

            // Create/update consent records with specified states.
            // When double opt-in is active, opted-in permissions are stored as PendingConfirmation until the user clicks the confirmation link.
            var hasChanges = false;
            foreach (var (permission, optedIn) in request.Permissions)
            {
                var status = (optedIn && doubleOptInActive) ? ConsentStatus.PendingConfirmation : (optedIn ? ConsentStatus.OptedIn : ConsentStatus.OptedOut);

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

            // Enqueue confirmation emails inside the transaction so that consent records
            // and queue entries are committed atomically. A failure here rolls back both.
            if (doubleOptInActive)
            {
                var baseUrl = !string.IsNullOrEmpty(instanceOptions.PublicUrl)
                    ? instanceOptions.PublicUrl.TrimEnd('/')
                    : $"{context.Request.Scheme}://{context.Request.Host}";

                var normalizedEmail = request.Email.Trim().ToLowerInvariant();
                var encryptedEmail = encryptor.Encrypt(normalizedEmail);
                var optedInPermissions = request.Permissions.Where(p => p.Value).Select(p => p.Key).ToList();

                if (optedInPermissions.Count > 0)
                {
                    if (config.PerPermissionEmail)
                    {
                        foreach (var permission in optedInPermissions)
                        {
                            await emailQueueRepo.CancelPendingAsync(normalizedBucket, emailHash, permission);

                            var confirmationToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
                            await emailQueueRepo.EnqueueAsync(new EmailQueueEntry
                            {
                                Bucket = normalizedBucket,
                                EncryptedEmail = encryptedEmail,
                                EmailHash = emailHash,
                                Permission = permission,
                                Language = tokenOptions.Language,
                                ConfirmationToken = confirmationToken,
                                ConfirmationUrl = $"{baseUrl}/confirm/{confirmationToken}",
                                ExpiresAt = DateTime.UtcNow.AddDays(7)
                            });
                        }
                    }
                    else
                    {
                        // Sort permissions so the key is stable regardless of input order.
                        var allPermissions = string.Join(",", optedInPermissions.OrderBy(p => p));

                        // Cancel any previous pending entries — both the combined key and each
                        // individual permission, to catch entries queued with a different set.
                        foreach (var permission in optedInPermissions)
                            await emailQueueRepo.CancelPendingAsync(normalizedBucket, emailHash, permission);
                        await emailQueueRepo.CancelPendingAsync(normalizedBucket, emailHash, allPermissions);

                        var confirmationToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
                        await emailQueueRepo.EnqueueAsync(new EmailQueueEntry
                        {
                            Bucket = normalizedBucket,
                            EncryptedEmail = encryptedEmail,
                            EmailHash = emailHash,
                            Permission = allPermissions,
                            Language = tokenOptions.Language,
                            ConfirmationToken = confirmationToken,
                            ConfirmationUrl = $"{baseUrl}/confirm/{confirmationToken}",
                            ExpiresAt = DateTime.UtcNow.AddDays(7)
                        });
                    }

                    logger.LogInformation(
                        "Confirmation emails queued: bucket={Bucket}, id={EmailId}, permissions={Permissions}, perPermission={PerPermission}",
                        normalizedBucket, emailHash[..12], string.Join(",", optedInPermissions), config.PerPermissionEmail);
                }
            }

            await consentService.CommitTransactionAsync();

            if (hasChanges)
            {
                await TriggerWebhookSafe(webhookService, consentRepository, request.Bucket, request.Email, emailHash, customFieldsJson);
                await notifications.PublishConsentUpdateAsync(new ConsentUpdateNotification(request.Bucket));
            }

            var emailId = emailHash[..12];
            logger.LogInformation(
                "Token generated: bucket={Bucket}, id={EmailId}, permissions={Permissions}, allowReplay={AllowReplay}, expiryDays={ExpiryDays}, skipUpdate={SkipUpdate}, doubleOptIn={DoubleOptIn}, timestamp={Timestamp}",
                request.Bucket,
                emailId,
                string.Join(",", request.Permissions.Select(p => $"{p.Key}:{(p.Value ? "in" : "out")}")),
                request.AllowReplay,
                request.ExpiryDays,
                request.SkipPermissionUpdate,
                doubleOptInActive,
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
        [FromServices] IConsentRepository repository,
        [FromServices] IBucketRepository bucketRepository)
    {
        var buckets = await repository.GetBucketsAsync();
        var explicitBucketNames = await bucketRepository.GetAllBucketNamesAsync();
        var result = new List<object>();
        var seen = new HashSet<string>();

        foreach (var b in buckets)
        {
            seen.Add(b.Name);
            var explicitPerms = await bucketRepository.GetPermissionsAsync(b.Name);
            var mergedPerms = b.Permissions.Union(explicitPerms).OrderBy(p => p).ToList();
            var isArchived = await bucketRepository.IsArchivedAsync(b.Name);
            result.Add(new { name = b.Name, totalEmails = b.TotalEmails, permissions = mergedPerms, isArchived });
        }

        // Include buckets that only exist in BucketPermissions (no consent records yet)
        foreach (var name in explicitBucketNames.Where(n => !seen.Contains(n)))
        {
            var explicitPerms = await bucketRepository.GetPermissionsAsync(name);
            var isArchived = await bucketRepository.IsArchivedAsync(name);
            result.Add(new { name, totalEmails = 0, permissions = (IReadOnlyList<string>)explicitPerms, isArchived });
        }

        return Results.Ok(result.OrderBy(r => ((dynamic)r).name));
    }

    private static async Task<IResult> GetBucketDetails(
        string bucket,
        [FromServices] IConsentRepository repository,
        [FromServices] IBucketRepository bucketRepository)
    {
        var bucketValidation = InputValidator.ValidateBucket(bucket);
        if (!bucketValidation.IsValid)
        {
            return Results.BadRequest(new { error = bucketValidation.Error });
        }

        var normalizedBucket = bucket.Trim().ToLowerInvariant();
        var details = await repository.GetBucketDetailsAsync(normalizedBucket);
        var explicitPerms = await bucketRepository.GetPermissionsAsync(normalizedBucket);
        var mergedPerms = details.Permissions.Union(explicitPerms).OrderBy(p => p).ToList();
        var isArchived = await bucketRepository.IsArchivedAsync(normalizedBucket);

        // Include explicit-only permissions in stats with zero counts
        var statsDict = details.Stats.ToDictionary(s => s.Permission);
        var mergedStats = mergedPerms.Select(p => statsDict.TryGetValue(p, out var s) ? s : new PermissionStats { Permission = p, OptedIn = 0, OptedOut = 0 }).ToList();

        return Results.Ok(new
        {
            name = details.Name,
            permissions = mergedPerms,
            stats = mergedStats,
            isArchived
        });
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
        [FromServices] IBucketRepository bucketRepository,
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

        try
        {
            using var transaction = await repository.BeginTransactionAsync();

            var deleted = await repository.DeleteBucketAsync(normalizedBucket);
            await bucketRepository.DeleteBucketAsync(normalizedBucket);

            await repository.CommitTransactionAsync();

            return Results.Ok(new { message = "Bucket deleted", recordsDeleted = deleted });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting bucket {Bucket}", normalizedBucket);
            return Results.StatusCode(500);
        }
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

    private static async Task<IResult> DeletePermission(
        string bucket,
        string permission,
        [FromServices] IConsentRepository repository,
        [FromServices] IBucketRepository bucketRepository,
        [FromServices] IAdminNotificationService notifications,
        ILogger<Program> logger)
    {
        var bucketValidation = InputValidator.ValidateBucket(bucket);
        if (!bucketValidation.IsValid)
        {
            return Results.BadRequest(new { error = bucketValidation.Error });
        }

        var permissionValidation = InputValidator.ValidatePermission(permission);
        if (!permissionValidation.IsValid)
        {
            return Results.BadRequest(new { error = permissionValidation.Error });
        }

        var normalizedBucket = bucket.Trim().ToLowerInvariant();
        var normalizedPermission = permission.Trim().ToLowerInvariant();

        logger.LogInformation(
            "Permission deletion: bucket={Bucket}, permission={Permission}, timestamp={Timestamp}",
            normalizedBucket,
            normalizedPermission,
            DateTime.UtcNow);

        try
        {
            using var transaction = await repository.BeginTransactionAsync();

            var deleted = await repository.DeletePermissionAsync(normalizedBucket, normalizedPermission);
            await bucketRepository.RemovePermissionAsync(normalizedBucket, normalizedPermission);

            await repository.CommitTransactionAsync();

            await notifications.PublishConsentUpdateAsync(new ConsentUpdateNotification(normalizedBucket));

            return Results.Ok(new { message = "Permission deleted", recordsDeleted = deleted });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting permission {Permission} from bucket {Bucket}", normalizedPermission, normalizedBucket);
            return Results.StatusCode(500);
        }
    }

    private static async Task<IResult> ArchiveBucket(
        string bucket,
        [FromServices] IBucketRepository bucketRepository,
        ILogger<Program> logger)
    {
        var bucketValidation = InputValidator.ValidateBucket(bucket);
        if (!bucketValidation.IsValid)
        {
            return Results.BadRequest(new { error = bucketValidation.Error });
        }

        var normalizedBucket = bucket.Trim().ToLowerInvariant();

        logger.LogInformation(
            "Bucket archived: bucket={Bucket}, timestamp={Timestamp}",
            normalizedBucket,
            DateTime.UtcNow);

        await bucketRepository.ArchiveAsync(normalizedBucket);
        return Results.Ok(new { success = true });
    }

    private static async Task<IResult> UnarchiveBucket(
        string bucket,
        [FromServices] IBucketRepository bucketRepository,
        ILogger<Program> logger)
    {
        var bucketValidation = InputValidator.ValidateBucket(bucket);
        if (!bucketValidation.IsValid)
        {
            return Results.BadRequest(new { error = bucketValidation.Error });
        }

        var normalizedBucket = bucket.Trim().ToLowerInvariant();

        logger.LogInformation(
            "Bucket unarchived: bucket={Bucket}, timestamp={Timestamp}",
            normalizedBucket,
            DateTime.UtcNow);

        await bucketRepository.UnarchiveAsync(normalizedBucket);
        return Results.Ok(new { success = true });
    }

    private static async Task<IResult> AddBucketPermission(
        string bucket,
        [FromBody] AddPermissionRequest request,
        [FromServices] IBucketRepository bucketRepository,
        ILogger<Program> logger)
    {
        var bucketValidation = InputValidator.ValidateBucket(bucket);
        if (!bucketValidation.IsValid)
        {
            return Results.BadRequest(new { error = bucketValidation.Error });
        }

        if (string.IsNullOrWhiteSpace(request.Permission))
        {
            return Results.BadRequest(new { error = "Permission name is required" });
        }

        var permissionValidation = InputValidator.ValidatePermission(request.Permission);
        if (!permissionValidation.IsValid)
        {
            return Results.BadRequest(new { error = permissionValidation.Error });
        }

        var normalizedBucket = bucket.Trim().ToLowerInvariant();
        var normalizedPermission = request.Permission.Trim().ToLowerInvariant();

        var added = await bucketRepository.AddPermissionAsync(normalizedBucket, normalizedPermission);
        if (!added)
        {
            return Results.Conflict(new { error = "Permission already exists in this bucket" });
        }

        logger.LogInformation(
            "Permission added: bucket={Bucket}, permission={Permission}, timestamp={Timestamp}",
            normalizedBucket,
            normalizedPermission,
            DateTime.UtcNow);

        return Results.Ok(new { success = true });
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

    private static async Task<IResult> GetWebhookBuckets(
        [FromServices] IWebhookService webhookService)
    {
        var bucketNames = await webhookService.GetWebhookBucketsAsync();
        return Results.Ok(bucketNames);
    }

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
        [FromServices] IWebhookService webhookService,
        [FromServices] IBucketRepository bucketRepository)
    {
        var bucketValidation = InputValidator.ValidateBucket(bucket);
        if (!bucketValidation.IsValid)
        {
            return Results.BadRequest(new { error = bucketValidation.Error });
        }

        if (await bucketRepository.IsArchivedAsync(bucket.Trim().ToLowerInvariant()))
        {
            return Results.Conflict(new { error = "Bucket is archived" });
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
        [FromServices] IWebhookService webhookService,
        [FromServices] IBucketRepository bucketRepository)
    {
        var bucketValidation = InputValidator.ValidateBucket(bucket);
        if (!bucketValidation.IsValid)
        {
            return Results.BadRequest(new { error = bucketValidation.Error });
        }

        if (await bucketRepository.IsArchivedAsync(bucket.Trim().ToLowerInvariant()))
        {
            return Results.Conflict(new { error = "Bucket is archived" });
        }

        await webhookService.DeleteWebhookConfigAsync(bucket.Trim().ToLowerInvariant());
        return Results.Ok(new { success = true });
    }

    private static async Task<IResult> GetWebhookErrors(
        string bucket,
        [FromServices] IWebhookRepository webhookRepository)
    {
        var bucketValidation = InputValidator.ValidateBucket(bucket);
        if (!bucketValidation.IsValid)
        {
            return Results.BadRequest(new { error = bucketValidation.Error });
        }

        var errors = await webhookRepository.GetRecentErrorsAsync(bucket.Trim().ToLowerInvariant());
        return Results.Ok(errors.Select(e => new
        {
            id = e.Id,
            bucket = e.Bucket,
            errorMessage = e.ErrorMessage,
            statusCode = e.StatusCode,
            occurredAt = e.OccurredAt,
            requestUrl = e.RequestUrl,
            requestMethod = e.RequestMethod,
            attemptCount = e.AttemptCount,
            stackTrace = e.StackTrace
        }));
    }

    private static async Task<IResult> DeleteWebhookError(
        string bucket,
        Guid id,
        [FromServices] IWebhookRepository webhookRepository)
    {
        var bucketValidation = InputValidator.ValidateBucket(bucket);
        if (!bucketValidation.IsValid)
        {
            return Results.BadRequest(new { error = bucketValidation.Error });
        }

        await webhookRepository.DeleteErrorAsync(id);
        return Results.Ok(new { success = true });
    }

    private static async Task<IResult> ClearWebhookErrors(
        string bucket,
        [FromServices] IWebhookRepository webhookRepository)
    {
        var bucketValidation = InputValidator.ValidateBucket(bucket);
        if (!bucketValidation.IsValid)
        {
            return Results.BadRequest(new { error = bucketValidation.Error });
        }

        await webhookRepository.ClearErrorsAsync(bucket.Trim().ToLowerInvariant());
        return Results.Ok(new { success = true });
    }

    private static async Task StreamEvents(
        HttpContext context,
        [FromServices] IAdminNotificationService notifications)
    {
        context.Response.Headers.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers.Connection = "keep-alive";

        var cancellationToken = context.RequestAborted;

        try
        {
            await foreach (var notification in notifications.SubscribeAllAsync(cancellationToken))
            {
                string eventType;
                string json;

                switch (notification)
                {
                    case WebhookErrorNotification webhook:
                        eventType = "webhook-error";
                        json = JsonSerializer.Serialize(new
                        {
                            bucket = webhook.Bucket,
                            errorMessage = webhook.ErrorMessage,
                            statusCode = webhook.StatusCode,
                            occurredAt = webhook.OccurredAt
                        });
                        break;
                    case ConsentUpdateNotification consentUpdate:
                        eventType = "consent-update";
                        json = JsonSerializer.Serialize(new
                        {
                            bucket = consentUpdate.Bucket
                        });
                        break;
                    default:
                        continue;
                }

                await context.Response.WriteAsync($"event: {eventType}\ndata: {json}\n\n", cancellationToken);
                await context.Response.Body.FlushAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Client disconnected, I think that's expected for SSE connections
        }
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

    private static async Task<IResult> GetIdentities(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortDir = null,
        [FromQuery] string? search = null,
        [FromQuery] string? searchType = null,
        [FromServices] IConsentRepository repository = null!,
        [FromServices] Encryptor encryptor = null!)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 10;
        if (pageSize > 100) pageSize = 100;

        if (searchType == "email" && !string.IsNullOrWhiteSpace(search))
        {
            var searchLower = search.Trim().ToLowerInvariant();
            var mappings = await repository.GetEmailHashMappingsAsync();
            var matchingHashes = mappings
                .Where(m =>
                {
                    if (m.EncryptedEmail is null) return false;
                    try
                    {
                        var email = encryptor.Decrypt(m.EncryptedEmail);
                        return email.Contains(searchLower, StringComparison.OrdinalIgnoreCase);
                    }
                    catch { return false; }
                })
                .Select(m => m.EmailHash)
                .ToList();

            var emailResult = await repository.GetIdentitiesByHashesAsync(matchingHashes, page, pageSize, sortBy, sortDir);
            return Results.Ok(emailResult);
        }

        var result = await repository.GetIdentitiesAsync(page, pageSize, sortBy, sortDir, search);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetIdentityDetails(
        string emailHash,
        [FromServices] IConsentRepository repository = null!,
        [FromServices] Encryptor encryptor = null!)
    {
        var validation = InputValidator.ValidateEmailHash(emailHash);
        if (!validation.IsValid)
            return Results.BadRequest(new { error = validation.Error });

        var details = await repository.GetIdentityDetailsAsync(emailHash.ToLowerInvariant());
        if (details == null)
            return Results.NotFound();

        string? email = null;
        if (!string.IsNullOrEmpty(details.EncryptedEmail))
        {
            try { email = encryptor.Decrypt(details.EncryptedEmail); }
            catch { }
        }

        return Results.Ok(new
        {
            details.EmailHash,
            Email = email,
            details.Subscriptions
        });
    }

    private static IResult GetSystemConfiguration(
        [FromServices] ISystemConfigurationService configService,
        [FromServices] Encryptor encryptor)
    {
        var config = configService.Get();
        
        // Mask sensitive fields with descriptive hints
        config.EmailResendApiKey = MaskSecret(encryptor.Decrypt(config.EmailResendApiKey));
        config.EmailSmtpPassword = MaskSecret(encryptor.Decrypt(config.EmailSmtpPassword));
        
        return Results.Ok(config);
    }

    private static async Task<IResult> SaveSystemConfiguration(
        [FromBody] SystemConfig config,
        [FromServices] ISystemConfigurationService configService,
        [FromServices] Encryptor encryptor)
    {
        var existing = configService.Get();

        // Handle sensitive fields: only update if not submitted as a mask
        // We compare the submitted value with what the display mask for the EXISTING secret would be
        var existingResendMask = MaskSecret(encryptor.Decrypt(existing.EmailResendApiKey));
        if (config.EmailResendApiKey == existingResendMask)
        {
            config.EmailResendApiKey = existing.EmailResendApiKey;
        }
        else if (!string.IsNullOrEmpty(config.EmailResendApiKey))
        {
            config.EmailResendApiKey = encryptor.Encrypt(config.EmailResendApiKey);
        }

        var existingSmtpMask = MaskSecret(encryptor.Decrypt(existing.EmailSmtpPassword));
        if (config.EmailSmtpPassword == existingSmtpMask)
        {
            config.EmailSmtpPassword = existing.EmailSmtpPassword;
        }
        else if (!string.IsNullOrEmpty(config.EmailSmtpPassword))
        {
            config.EmailSmtpPassword = encryptor.Encrypt(config.EmailSmtpPassword);
        }

        await configService.SaveAsync(config);
        
        var saved = configService.Get();
        saved.EmailResendApiKey = MaskSecret(encryptor.Decrypt(saved.EmailResendApiKey));
        saved.EmailSmtpPassword = MaskSecret(encryptor.Decrypt(saved.EmailSmtpPassword));
        
        return Results.Ok(saved);
    }

    private static string MaskSecret(string? decryptedValue)
    {
        if (string.IsNullOrEmpty(decryptedValue)) return string.Empty;
        
        // Resend keys: re_12345678... (show prefix + 8 chars)
        if (decryptedValue.StartsWith("re_", StringComparison.OrdinalIgnoreCase) && decryptedValue.Length > 12)
        {
            return decryptedValue[..11] + "...";
        }

        // Generic secrets: first 4 chars + stars + last 4 chars if long enough
        if (decryptedValue.Length > 12)
        {
            return decryptedValue[..4] + "********" + decryptedValue[^4..];
        }

        return "********";
    }

    private static async Task<IResult> GetBucketOptions(
        string bucket,
        [FromServices] IBucketOptionsRepository repo)
    {
        return Results.Ok(await repo.GetAsync(bucket));
    }

    private static async Task<IResult> SaveBucketOptions(
        string bucket,
        [FromBody] BucketOptionsRequest request,
        [FromServices] IBucketOptionsRepository repo)
    {
        await repo.SaveAsync(new BucketOptions { Bucket = bucket, DoubleOptIn = request.DoubleOptIn, UpdatedAt = DateTime.UtcNow });
        return Results.Ok();
    }

    private sealed record BucketOptionsRequest(bool DoubleOptIn);

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

public sealed class AddPermissionRequest
{
    public string Permission { get; set; } = string.Empty;
}

public sealed class WebhookConfigRequest
{
    public string Url { get; set; } = string.Empty;
    public string Method { get; set; } = "POST";
    public Dictionary<string, string>? Headers { get; set; }
    public string? BodyTemplate { get; set; }
}
