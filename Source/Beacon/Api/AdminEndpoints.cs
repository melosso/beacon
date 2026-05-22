using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Beacon.Core.Models;
using Beacon.Core.Security;
using Beacon.Core.Services;
using Beacon.Core.Validation;
using Beacon.Storage;
using Beacon.Tokens;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Api;

internal sealed record ObjectStorageTestRequest(
    string Provider,
    string Endpoint,
    string Bucket,
    string Region,
    string AccessKey,
    string SecretKey);

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
            .WithDescription("Generate preference management tokens. Accepts an array of requests. Returns an array of URL-safe tokens for the /u/{token} endpoint.");

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
            .RequireAuthorization("Admin")
            .ExcludeFromDescription();

        routes.MapDelete("/api/admin/buckets/{bucket}/webhook", DeleteWebhookConfig)
            .RequireAuthorization("Admin")
            .ExcludeFromDescription();

        routes.MapGet("/api/admin/buckets/{bucket}/webhook/errors", GetWebhookErrors)
            .RequireAuthorization()
            .ExcludeFromDescription();

        routes.MapDelete("/api/admin/buckets/{bucket}/webhook/errors/{id}", DeleteWebhookError)
            .RequireAuthorization("Admin")
            .ExcludeFromDescription();

        routes.MapDelete("/api/admin/buckets/{bucket}/webhook/errors", ClearWebhookErrors)
            .RequireAuthorization("Admin")
            .ExcludeFromDescription();

        routes.MapGet("/api/admin/events", StreamEvents)
            .RequireAuthorization()
            .ExcludeFromDescription();

        routes.MapGet("/api/admin/settings", GetSystemConfiguration)
            .RequireAuthorization("Admin")
            .ExcludeFromDescription();

        routes.MapPut("/api/admin/settings", SaveSystemConfiguration)
            .RequireAuthorization("Admin")
            .ExcludeFromDescription();

        routes.MapPost("/api/admin/settings/object-storage/test", TestObjectStorageConnection)
            .RequireAuthorization("Admin")
            .ExcludeFromDescription();

        routes.MapDelete("/api/admin/cache", FlushCache)
            .RequireAuthorization("Admin")
            .ExcludeFromDescription();

        routes.MapGet("/api/admin/cache/stats", GetCacheStats)
            .RequireAuthorization("Admin")
            .ExcludeFromDescription();

        routes.MapGet("/api/admin/buckets/{bucket}/options", GetBucketOptions)
            .RequireAuthorization()
            .ExcludeFromDescription();

        routes.MapPut("/api/admin/buckets/{bucket}/options", SaveBucketOptions)
            .RequireAuthorization()
            .ExcludeFromDescription();

        routes.MapPost("/api/admin/buckets/{bucket}/tokens/export", ExportBucketTokens)
            .RequireAuthorization("Admin")
            .ExcludeFromDescription();

        routes.MapGet("/api/admin/data-policies/tasks", GetWorkflowTasks)
            .RequireAuthorization("Admin")
            .ExcludeFromDescription();

        routes.MapPost("/api/admin/data-policies/run", TriggerDataPolicies)
            .RequireAuthorization("Admin")
            .ExcludeFromDescription();

        routes.MapPost("/api/admin/data-policies/tasks/{id}/approve", ApproveWorkflowTask)
            .RequireAuthorization("Admin")
            .ExcludeFromDescription();

        routes.MapPost("/api/admin/data-policies/tasks/{id}/reject", RejectWorkflowTask)
            .RequireAuthorization("Admin")
            .ExcludeFromDescription();

        routes.MapGet("/api/admin/audit", GetAuditLog)
            .RequireAuthorization()
            .ExcludeFromDescription();
    }

    private static async Task<IResult> OverrideConsent(
        HttpContext context,
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

        var actorId = context.User.Identity?.Name;

        try
        {
            await consentService.OverrideAsync(request.Bucket, request.Email, request.Permission, status, customFieldsJson, actorId);

            await TriggerWebhookSafe(webhookService, consentRepository, request.Bucket, request.Email, emailHash, customFieldsJson);
            await notifications.PublishConsentUpdateAsync(new ConsentUpdateNotification(request.Bucket));

            return Results.Ok(new { message = "Consent updated" });
        }
        catch (DbUpdateException ex)
        {
            if (ex.InnerException?.Message?.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase) == true)
            {
                logger.LogWarning("Unique constraint violation during consent override for bucket={Bucket}, emailHash={EmailHash}, permission={Permission}: {ErrorMessage}", request.Bucket, emailHasher.Hash(request.Email), request.Permission, ex.InnerException?.Message);
                return Results.Conflict(new { error = "A record with the same email and permission already exists in this bucket." });
            }

            logger.LogError(ex, "Database update error during consent override for bucket={Bucket}, emailHash={EmailHash}, permission={Permission}", request.Bucket, emailHash, request.Permission);
            return Results.StatusCode(500); // Generic 500 for other database errors
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error occurred during consent override for bucket={Bucket}, emailHash={EmailHash}, permission={Permission}", request.Bucket, emailHash, request.Permission);
            return Results.StatusCode(500); // Generic 500 for other unexpected errors
        }
    }

    private static async Task<IResult> BatchOverrideConsent(
        HttpContext httpContext,
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

        var actorId = httpContext.User.Identity?.Name;

        try
        {
            using var transaction = await consentService.BeginTransactionAsync();

            foreach (var (permission, status) in request.Permissions)
            {
                if (!Enum.TryParse<ConsentStatus>(status, true, out var consentStatus) || consentStatus == ConsentStatus.PendingConfirmation)
                {
                    return Results.BadRequest(new { error = $"Invalid status '{status}' for permission '{permission}'" });
                }

                await consentService.OverrideAsync(normalizedBucket, request.Email, permission, consentStatus, customFieldsJson, actorId);
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
        [FromBody] List<GenerateTokenRequest> requests,
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
        [FromServices] Beacon.Storage.EmailDispatchTrigger emailDispatchTrigger,
        ILogger<Program> logger)
    {
        if (requests is null || requests.Count == 0)
            return Results.BadRequest(new { error = "At least one object is required." });

        // Validate all requests upfront before processing any
        foreach (var request in requests)
        {
            var bucketValidation = InputValidator.ValidateBucket(request.Bucket);
            if (!bucketValidation.IsValid)
                return Results.BadRequest(new { error = bucketValidation.Error });

            if (await bucketRepository.IsArchivedAsync(request.Bucket.Trim().ToLowerInvariant()))
                return Results.Conflict(new { error = "Bucket is archived" });

            var emailValidation = InputValidator.ValidateEmail(request.Email);
            if (!emailValidation.IsValid)
                return Results.BadRequest(new { error = emailValidation.Error });

            if (request.Permissions is null || request.Permissions.Count == 0)
                return Results.BadRequest(new { error = "At least one permission is required" });

            var permissionsValidation = InputValidator.ValidatePermissions(request.Permissions.Keys.ToArray());
            if (!permissionsValidation.IsValid)
                return Results.BadRequest(new { error = permissionsValidation.Error });

            if (!string.IsNullOrEmpty(request.Language) && !SupportedLanguages.Contains(request.Language.ToLowerInvariant()))
                return Results.BadRequest(new { error = $"Unsupported language code '{request.Language}'. Supported languages are: {string.Join(", ", SupportedLanguages)}" });
        }

        var config = configService.Get();
        var emailProviderNormalized = config.EmailProvider?.Trim().ToLowerInvariant() ?? string.Empty;
        var responses = new List<GenerateTokenResponse>(requests.Count);
        var actorId = context.User.Identity?.Name;

        try
        {
            foreach (var request in requests)
            {
                var doubleOptInActive = !instanceOptions.DisableEmailNotifications
                    && config.EnableDoubleOptIn
                    && config.EmailNotifications
                    && emailProviderNormalized is not ("none" or "");

                var normalizedBucket = request.Bucket.Trim().ToLowerInvariant();
                var emailHash = emailHasher.Hash(request.Email);
                var permissionNames = request.Permissions!.Keys.ToArray();

                if (doubleOptInActive)
                {
                    var bucketOpts = await bucketOptionsRepo.GetAsync(normalizedBucket);
                    doubleOptInActive = bucketOpts.DoubleOptIn;
                }

                if (request.SkipConfirmationEmail)
                    doubleOptInActive = false;

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
                        var created = await consentService.EnsureAsync(request.Bucket, request.Email, permission, status, customFieldsJson,
                            source: ConsentSource.Api, actorId: actorId);
                        if (created) hasChanges = true;
                    }
                    else
                    {
                        // Always upsert (insert or update)
                        await consentService.OverrideAsync(request.Bucket, request.Email, permission, status, customFieldsJson, actorId,
                            source: ConsentSource.Api);
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

                                var confirmationTokenPlain = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
                                var confirmationTokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(confirmationTokenPlain))).ToLowerInvariant();
                                await emailQueueRepo.EnqueueAsync(new EmailQueueEntry
                                {
                                    Bucket = normalizedBucket,
                                    EncryptedEmail = encryptedEmail,
                                    EmailHash = emailHash,
                                    Permission = permission,
                                    Language = tokenOptions.Language,
                                    ConfirmationToken = confirmationTokenHash,
                                    ConfirmationUrl = $"{baseUrl}/confirm/{confirmationTokenPlain}",
                                    ExpiresAt = DateTime.UtcNow.AddDays(7)
                                });
                            }
                        }
                        else
                        {
                            // Sort permissions so the key is stable regardless of input order.
                            var allPermissions = string.Join(",", optedInPermissions.OrderBy(p => p));

                            // Cancel any previous pending entries, both the combined key and each individual permission, to catch entries queued with a different set.
                            foreach (var permission in optedInPermissions)
                                await emailQueueRepo.CancelPendingAsync(normalizedBucket, emailHash, permission);
                            await emailQueueRepo.CancelPendingAsync(normalizedBucket, emailHash, allPermissions);

                            var confirmationTokenPlain = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
                            var confirmationTokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(confirmationTokenPlain))).ToLowerInvariant();
                            await emailQueueRepo.EnqueueAsync(new EmailQueueEntry
                            {
                                Bucket = normalizedBucket,
                                EncryptedEmail = encryptedEmail,
                                EmailHash = emailHash,
                                Permission = allPermissions,
                                Language = tokenOptions.Language,
                                ConfirmationToken = confirmationTokenHash,
                                ConfirmationUrl = $"{baseUrl}/confirm/{confirmationTokenPlain}",
                                ExpiresAt = DateTime.UtcNow.AddDays(7)
                            });
                        }

                        logger.LogInformation(
                            "Email queue: confirmation email(s) enqueued (bucket={Bucket}, id={EmailId}, permissions={Permissions}, perPermission={PerPermission})",
                            normalizedBucket, emailHash[..12], string.Join(",", optedInPermissions), config.PerPermissionEmail);
                    }
                }

                await consentService.CommitTransactionAsync();

                // Wake the email queue worker immediately if emails were just enqueued,
                // rather than waiting for the next cron tick.
                if (doubleOptInActive)
                    emailDispatchTrigger.Signal();

                if (hasChanges)
                {
                    _ = TriggerWebhookSafe(webhookService, consentRepository, request.Bucket, request.Email, emailHash, customFieldsJson);
                    await notifications.PublishConsentUpdateAsync(new ConsentUpdateNotification(request.Bucket));
                }

                logger.LogInformation(
                    "Token generated: bucket={Bucket}, id={EmailId}, permissions={Permissions}, allowReplay={AllowReplay}, expiryDays={ExpiryDays}, skipUpdate={SkipUpdate}, doubleOptIn={DoubleOptIn}, timestamp={Timestamp}",
                    request.Bucket,
                    emailHash[..12],
                    string.Join(",", request.Permissions.Select(p => $"{p.Key}:{(p.Value ? "in" : "out")}")),
                    request.AllowReplay,
                    request.ExpiryDays,
                    request.SkipPermissionUpdate,
                    doubleOptInActive,
                    DateTime.UtcNow);

                responses.Add(new GenerateTokenResponse { Token = token, DoubleOptIn = doubleOptInActive });
            }

            return Results.Ok(responses);
        }
        catch (DbUpdateException ex)
        {
            if (ex.InnerException?.Message?.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase) == true)
            {
                logger.LogWarning("Unique constraint violation during token generation: {ErrorMessage}", ex.InnerException?.Message);
                return Results.Conflict(new { error = "A record with the same email and permission already exists in this bucket." });
            }

            logger.LogError(ex, "Database update error during token generation");
            return Results.StatusCode(500);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error occurred during token generation");
            return Results.StatusCode(500);
        }
    }

    private static async Task<IResult> GetBuckets(
        [FromServices] IConsentRepository repository,
        [FromServices] IBucketRepository bucketRepository)
    {
        var buckets = await repository.GetBucketsAsync();
        var explicitBucketNames = await bucketRepository.GetAllBucketNamesAsync();
        var allExplicitPerms = await bucketRepository.GetAllPermissionsGroupedAsync();
        var archivedBuckets = await bucketRepository.GetArchivedBucketsAsync();
        var result = new List<object>();
        var seen = new HashSet<string>();

        foreach (var b in buckets)
        {
            seen.Add(b.Name);
            var explicitPerms = allExplicitPerms.TryGetValue(b.Name, out var ep) ? ep : [];
            var mergedPerms = b.Permissions.Union(explicitPerms).OrderBy(p => p).ToList();
            result.Add(new { name = b.Name, totalEmails = b.TotalEmails, permissions = mergedPerms, isArchived = archivedBuckets.Contains(b.Name) });
        }

        foreach (var name in explicitBucketNames.Where(n => !seen.Contains(n)))
        {
            var explicitPerms = allExplicitPerms.TryGetValue(name, out var ep) ? ep : (IReadOnlyList<string>)[];
            result.Add(new { name, totalEmails = 0, permissions = explicitPerms, isArchived = archivedBuckets.Contains(name) });
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
                r.Email != null && r.Email.Contains(searchLower, StringComparison.OrdinalIgnoreCase)
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
        [FromServices] IEmailQueueRepository emailQueueRepository,
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

        await emailQueueRepository.DeleteByEmailHashAsync(emailHash);
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
        config.EmailResendApiKey = MaskSecret(encryptor.Decrypt(config.EmailResendApiKey));
        config.EmailSmtpPassword = MaskSecret(encryptor.Decrypt(config.EmailSmtpPassword));
        config.ObjectStorageAccessKey = MaskSecret(encryptor.Decrypt(config.ObjectStorageAccessKey));
        config.ObjectStorageSecretKey = MaskSecret(encryptor.Decrypt(config.ObjectStorageSecretKey));
        return Results.Ok(config);
    }

    private static async Task<IResult> SaveSystemConfiguration(
        [FromBody] SystemConfig config,
        [FromServices] ISystemConfigurationService configService,
        [FromServices] Encryptor encryptor)
    {
        var existing = configService.Get();

        HandleSecret(config, existing, encryptor,
            c => c.EmailResendApiKey, (c, v) => c.EmailResendApiKey = v);
        HandleSecret(config, existing, encryptor,
            c => c.EmailSmtpPassword, (c, v) => c.EmailSmtpPassword = v);
        HandleSecret(config, existing, encryptor,
            c => c.ObjectStorageAccessKey, (c, v) => c.ObjectStorageAccessKey = v);
        HandleSecret(config, existing, encryptor,
            c => c.ObjectStorageSecretKey, (c, v) => c.ObjectStorageSecretKey = v);

        await configService.SaveAsync(config);

        var saved = configService.Get();
        saved.EmailResendApiKey = MaskSecret(encryptor.Decrypt(saved.EmailResendApiKey));
        saved.EmailSmtpPassword = MaskSecret(encryptor.Decrypt(saved.EmailSmtpPassword));
        saved.ObjectStorageAccessKey = MaskSecret(encryptor.Decrypt(saved.ObjectStorageAccessKey));
        saved.ObjectStorageSecretKey = MaskSecret(encryptor.Decrypt(saved.ObjectStorageSecretKey));

        return Results.Ok(saved);
    }

    private static void HandleSecret(
        SystemConfig incoming,
        SystemConfig existing,
        Encryptor encryptor,
        Func<SystemConfig, string> getter,
        Action<SystemConfig, string> setter)
    {
        var existingMask = MaskSecret(encryptor.Decrypt(getter(existing)));
        var submitted = getter(incoming);
        if (submitted == existingMask)
            setter(incoming, getter(existing));
        else if (!string.IsNullOrEmpty(submitted))
            setter(incoming, encryptor.Encrypt(submitted));
    }

    private static async Task<IResult> TestObjectStorageConnection(
        [FromBody] ObjectStorageTestRequest request,
        CancellationToken ct)
    {
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            using var client = BuildTestS3Client(request);
            await client.GetBucketLocationAsync(request.Bucket, ct);
            sw.Stop();

            return Results.Ok(new
            {
                success = true,
                message = "Connection successful.",
                latencyMs = (int)sw.ElapsedMilliseconds
            });
        }
        catch (Exception ex)
        {
            return Results.Ok(new
            {
                success = false,
                message = ex.Message,
                latencyMs = 0
            });
        }
    }

    private static Amazon.S3.AmazonS3Client BuildTestS3Client(ObjectStorageTestRequest req)
    {
        var credentials = new Amazon.Runtime.BasicAWSCredentials(req.AccessKey, req.SecretKey);
        var s3Config = new Amazon.S3.AmazonS3Config
        {
            ForcePathStyle = req.Provider is "r2" or "minio"
        };
        if (!string.IsNullOrWhiteSpace(req.Endpoint))
            s3Config.ServiceURL = req.Endpoint;
        else if (!string.IsNullOrWhiteSpace(req.Region))
            s3Config.RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(req.Region);
        return new Amazon.S3.AmazonS3Client(credentials, s3Config);
    }

    private static async Task<IResult> FlushCache(
        [FromServices] IBeaconCacheService cache,
        CancellationToken ct)
    {
        await cache.FlushAsync(ct);
        return Results.NoContent();
    }

    private static IResult GetCacheStats(
        [FromServices] IBeaconCacheService cache,
        [FromServices] ISystemConfigurationService configService)
    {
        var cfg = configService.Get();
        return Results.Ok(new
        {
            enabled = cfg.EnableCaching,
            provider = cfg.EnableCaching ? "memory" : "none",
            approximateKeyCount = cache.KeyCount
        });
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
        await repo.SaveAsync(new BucketOptions
        {
            Bucket = bucket,
            DoubleOptIn = request.DoubleOptIn,
            UtmCampaign = string.IsNullOrWhiteSpace(request.UtmCampaign) ? null : request.UtmCampaign.Trim(),
            UpdatedAt = DateTime.UtcNow
        });
        return Results.Ok();
    }

    private sealed record BucketOptionsRequest(bool DoubleOptIn, string? UtmCampaign = null);

    private static async Task<IResult> ExportBucketTokens(
        string bucket,
        [FromBody] ExportBucketTokensRequest request,
        [FromServices] IConsentRepository repository,
        [FromServices] TokenGenerator generator,
        [FromServices] Encryptor encryptor,
        [FromServices] Beacon.Core.Services.InstanceOptions instanceOptions,
        HttpContext context,
        CancellationToken ct)
    {
        var normalized = bucket.Trim().ToLowerInvariant();
        var records = await repository.GetAllBucketRecordsAsync(normalized);

        var baseUrl = (instanceOptions.PublicUrl
            ?? $"{context.Request.Scheme}://{context.Request.Host}").TrimEnd('/');

        var utmParts = new List<string>(3);
        if (!string.IsNullOrWhiteSpace(request.UtmCampaign))
            utmParts.Add($"utm_campaign={Uri.EscapeDataString(request.UtmCampaign)}");
        if (!string.IsNullOrWhiteSpace(request.UtmSource))
            utmParts.Add($"utm_source={Uri.EscapeDataString(request.UtmSource)}");
        if (!string.IsNullOrWhiteSpace(request.UtmMedium))
            utmParts.Add($"utm_medium={Uri.EscapeDataString(request.UtmMedium)}");
        var qs = utmParts.Count > 0 ? "?" + string.Join("&", utmParts) : string.Empty;

        var tokenOptions = new Tokens.GenerateTokenRequest
        {
            AllowReplay = true,
            ExpiryDays = request.ExpiryDays > 0 ? request.ExpiryDays : 30,
            Language = string.IsNullOrWhiteSpace(request.Language) ? "en" : request.Language
        };

        var rows = new List<(string Email, string Url)>(records.Count);

        foreach (var record in records)
        {
            if (string.IsNullOrEmpty(record.EncryptedEmail)) continue;

            string email;
            try { email = encryptor.Decrypt(record.EncryptedEmail); }
            catch { continue; }
            if (string.IsNullOrWhiteSpace(email)) continue;

            string[] permissionNames;
            if (!string.IsNullOrEmpty(request.Permission))
            {
                if (!record.Permissions.ContainsKey(request.Permission)) continue;
                permissionNames = [request.Permission];
            }
            else
            {
                permissionNames = [.. record.Permissions.Keys];
            }

            if (permissionNames.Length == 0) continue;

            var token = generator.Generate(normalized, email, permissionNames, tokenOptions);
            rows.Add((email, $"{baseUrl}/u/{token}{qs}"));
        }

        var format = (request.Format ?? "csv").ToLowerInvariant();
        var filename = $"unsubscribe-links-{normalized}.{format}";
        context.Response.Headers["Content-Disposition"] = $"attachment; filename=\"{filename}\"";

        return format switch
        {
            "json" => Results.Json(rows.Select(r => new { email = r.Email, url = r.Url })),
            "xml"  => Results.Content(BuildXml(rows), "application/xml"),
            _      => Results.Content(BuildCsv(rows), "text/csv")
        };
    }

    private static string BuildCsv(IEnumerable<(string Email, string Url)> rows)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("email,url");
        foreach (var (email, url) in rows)
            sb.AppendLine($"{CsvEscape(email)},{CsvEscape(url)}");
        return sb.ToString();
    }

    private static string BuildXml(IEnumerable<(string Email, string Url)> rows)
    {
        var doc = new System.Xml.Linq.XDocument(
            new System.Xml.Linq.XElement("subscribers",
                rows.Select(r => new System.Xml.Linq.XElement("subscriber",
                    new System.Xml.Linq.XElement("email", r.Email),
                    new System.Xml.Linq.XElement("url", r.Url)))));
        return doc.ToString();
    }

    private static string CsvEscape(string value) =>
        value.AsSpan().IndexOfAny(',', '"', '\n') >= 0
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;

    internal sealed record ExportBucketTokensRequest(
        string? Permission = null,
        string? UtmCampaign = null,
        string? UtmSource = null,
        string? UtmMedium = null,
        int ExpiryDays = 30,
        string? Language = null,
        string Format = "csv"); // "csv" | "json" | "xml"

    private static async Task<IResult> GetWorkflowTasks(
        [FromQuery] int limit,
        [FromServices] IWorkflowTaskRepository taskRepo,
        CancellationToken ct)
    {
        var effectiveLimit = limit > 0 ? Math.Min(limit, 200) : 50;
        return Results.Ok(await taskRepo.GetRecentAsync(effectiveLimit, ct));
    }

    private static IResult TriggerDataPolicies(
        [FromServices] DataPolicyTrigger trigger)
    {
        trigger.Signal();
        return Results.Accepted();
    }

    private static async Task<IResult> ApproveWorkflowTask(
        Guid id,
        [FromServices] DataPolicyService dataPolicySvc,
        CancellationToken ct)
    {
        var result = await dataPolicySvc.ApproveTaskAsync(id, ct);
        return result.Outcome switch
        {
            TaskOperationOutcome.NotFound => Results.NotFound(),
            TaskOperationOutcome.InvalidStatus => Results.BadRequest("Task is not pending approval."),
            _ => Results.Ok(result.Task)
        };
    }

    private static async Task<IResult> RejectWorkflowTask(
        Guid id,
        [FromServices] DataPolicyService dataPolicySvc,
        CancellationToken ct)
    {
        var result = await dataPolicySvc.RejectTaskAsync(id, ct);
        return result.Outcome switch
        {
            TaskOperationOutcome.NotFound => Results.NotFound(),
            TaskOperationOutcome.InvalidStatus => Results.BadRequest("Task is not pending approval."),
            _ => Results.Ok(result.Task)
        };
    }

    private static async Task<IResult> GetAuditLog(
        [FromQuery] string? bucket,
        [FromQuery] string? emailHash,
        [FromQuery] int page = 1,
        [FromQuery] int size = 25,
        [FromServices] IConsentRepository repository = null!,
        CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        size = Math.Clamp(size, 1, 100);
        var normalizedBucket = string.IsNullOrWhiteSpace(bucket) ? null : bucket.Trim().ToLowerInvariant();
        var normalizedHash = string.IsNullOrWhiteSpace(emailHash) ? null : emailHash.Trim().ToLowerInvariant();

        var result = await repository.GetAuditAsync(normalizedBucket, normalizedHash, page, size, ct);

        return Results.Ok(new
        {
            records = result.Records.Select(e => new
            {
                id = e.Id,
                bucket = e.Bucket,
                emailHash = e.EmailHash,
                displayId = e.EmailHash[..Math.Min(16, e.EmailHash.Length)] + "...",
                permission = e.Permission,
                oldStatus = e.OldStatus?.ToString(),
                newStatus = e.NewStatus.ToString(),
                source = e.Source.ToString(),
                actorId = e.ActorId,
                changedAt = e.ChangedAt,
                customFields = e.CustomFields
            }),
            total = result.Total,
            page = result.Page,
            pageSize = result.PageSize
        });
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

    /// <summary>
    /// When true, bypasses the double opt-in confirmation email for this specific record,
    /// even if double opt-in is enabled for the bucket. Default: false (send emails).
    /// </summary>
    public bool SkipConfirmationEmail { get; set; } = false;
}

public sealed class GenerateTokenResponse
{
    public string Token { get; set; } = string.Empty;
    public bool DoubleOptIn { get; set; }
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
