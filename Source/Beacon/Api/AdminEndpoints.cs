using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Beacon.Core.Models;
using Beacon.Core.Security;
using Beacon.Core.Services;
using Beacon.Middleware;
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

    private static readonly HashSet<string> _allowedThemes = ["system", "light", "dark"];
    private static readonly HashSet<string> _allowedFonts = ["Arial", "Helvetica", "Georgia", "Tahoma", "Verdana", "Trebuchet MS", "Courier New", "Inter", "Manrope"];
    private static readonly Regex _hexColour = new(@"^#[0-9a-fA-F]{6}$", RegexOptions.Compiled);
    private static readonly HashSet<string> _allowedLogoTypes = ["base64", "objectStorage", "url"];

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

        routes.MapPost("/api/admin/identities/export", ExportIdentities)
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

        routes.MapPost("/api/admin/buckets/{bucket}/records/export", ExportBucketRecords)
            .RequireAuthorization()
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

        routes.MapGet("/api/admin/brand-identities", GetBrandIdentities)
            .RequireAuthorization("Admin")
            .ExcludeFromDescription();

        routes.MapPost("/api/admin/brand-identities", CreateBrandIdentity)
            .RequireAuthorization("Admin")
            .ExcludeFromDescription();

        routes.MapPut("/api/admin/brand-identities/{id:int}", UpdateBrandIdentity)
            .RequireAuthorization("Admin")
            .ExcludeFromDescription();

        routes.MapDelete("/api/admin/brand-identities/{id:int}", DeleteBrandIdentity)
            .RequireAuthorization("Admin")
            .ExcludeFromDescription();

        routes.MapPut("/api/admin/brand-identities/{id:int}/buckets", AssignBrandIdentityBuckets)
            .RequireAuthorization("Admin")
            .ExcludeFromDescription();

        routes.MapPost("/api/admin/assets/logo", UploadLogo)
            .RequireAuthorization("Admin")
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

        if (request.Name != null && request.Name.Length > 250)
            return Results.BadRequest(new { error = "Name must be 250 characters or fewer" });

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
            await consentService.OverrideAsync(request.Bucket, request.Email, request.Permission, status, customFieldsJson, actorId, name: request.Name);

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

        if (request.Name != null && request.Name.Length > 250)
            return Results.BadRequest(new { error = "Name must be 250 characters or less." });

        var normalizedBucket = bucket.Trim().ToLowerInvariant();
        var emailHash = emailHasher.Hash(request.Email);

        string? customFieldsJson = request.CustomFields is { Count: > 0 }
            ? JsonSerializer.Serialize(request.CustomFields)
            : null;

        var actorId = httpContext.User.Identity?.Name;
        var effectiveName = string.IsNullOrWhiteSpace(request.Name) ? null : request.Name.Trim();

        try
        {
            using var transaction = await consentService.BeginTransactionAsync();

            foreach (var (permission, status) in request.Permissions)
            {
                if (!Enum.TryParse<ConsentStatus>(status, true, out var consentStatus) || consentStatus == ConsentStatus.PendingConfirmation)
                {
                    return Results.BadRequest(new { error = $"Invalid status '{status}' for permission '{permission}'" });
                }

                await consentService.OverrideAsync(normalizedBucket, request.Email, permission, consentStatus, customFieldsJson, actorId, name: effectiveName);
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
                            source: ConsentSource.Api, actorId: actorId, name: request.Name);
                        if (created) hasChanges = true;
                    }
                    else
                    {
                        // Always upsert (insert or update)
                        await consentService.OverrideAsync(request.Bucket, request.Email, permission, status, customFieldsJson, actorId,
                            source: ConsentSource.Api, name: request.Name);
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
        [FromServices] IBucketRepository bucketRepository,
        [FromServices] IBrandIdentityService brandService)
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

        var identity = await brandService.GetForBucketAsync(normalizedBucket);
        object? brandIdentity = null;
        if (!identity.IsDefault)
        {
            string? accent = null;
            try { accent = JsonSerializer.Deserialize<BrandIdentitySettings>(identity.Settings)?.PrimaryAccent; } catch { }
            brandIdentity = new { name = identity.Name, accent };
        }

        return Results.Ok(new
        {
            name = details.Name,
            permissions = mergedPerms,
            stats = mergedStats,
            isArchived,
            brandIdentity
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

        // Decrypt emails and names for admin display
        foreach (var record in result.Records)
        {
            if (!string.IsNullOrEmpty(record.EncryptedEmail))
                try { record.Email = encryptor!.Decrypt(record.EncryptedEmail); } catch { }
            if (!string.IsNullOrEmpty(record.EncryptedName))
                try { record.Name = encryptor!.Decrypt(record.EncryptedName); } catch { }
        }

        // If searching by email, filter after decryption
        var records = result.Records;
        var total = result.Total;
        if (searchType == "email" && !string.IsNullOrWhiteSpace(search))
        {
            var searchLower = search.ToLowerInvariant();
            records = records.Where(r => MatchesWildcard(r.Email, searchLower)).ToList();
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
        var normalizedPermission = permission.Trim().ToLowerInvariant().Replace(' ', '_');

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
        var normalizedPermission = request.Permission.Trim().ToLowerInvariant().Replace(' ', '_');

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

    private static readonly HashSet<string> ValidIdentitySortFields =
        new(StringComparer.OrdinalIgnoreCase) { "id", "buckets", "lastchanged", "email", "firstseen" };

    private static async Task<IResult> GetIdentities(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortDir = null,
        [FromQuery] string? search = null,
        [FromQuery] string? searchType = null,
        [FromServices] IConsentRepository repository = null!,
        [FromServices] Encryptor encryptor = null!,
        CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 10;
        if (pageSize > 100) pageSize = 100;

        if (search != null && search.Length > 200)
            return Results.BadRequest(new { error = "Search query too long" });
        if (searchType != null && searchType is not ("id" or "email"))
            return Results.BadRequest(new { error = "Invalid searchType" });
        if (sortDir != null && sortDir is not ("asc" or "desc"))
            return Results.BadRequest(new { error = "Invalid sortDir" });
        if (sortBy != null && !ValidIdentitySortFields.Contains(sortBy))
            return Results.BadRequest(new { error = "Invalid sortBy field" });

        PagedResult<IdentityInfo> result;

        if (string.Equals(sortBy, "email", StringComparison.OrdinalIgnoreCase))
        {
            // Deferred: decrypt all emails in parallel, sort in memory, then paginate
            result = await GetIdentitiesSortedByEmailAsync(
                page, pageSize, sortDir, search, searchType, repository, encryptor, ct);
        }
        else if (searchType == "email" && !string.IsNullOrWhiteSpace(search))
        {
            var searchLower = search.Trim().ToLowerInvariant();
            var mappings = await repository.GetEmailHashMappingsAsync();
            var matchingHashes = new System.Collections.Concurrent.ConcurrentBag<string>();
            await Parallel.ForEachAsync(mappings,
                new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = ct },
                (mapping, token) =>
                {
                    if (mapping.EncryptedEmail is null) return ValueTask.CompletedTask;
                    try
                    {
                        var email = encryptor.Decrypt(mapping.EncryptedEmail);
                        if (email.Contains(searchLower, StringComparison.OrdinalIgnoreCase))
                            matchingHashes.Add(mapping.EmailHash);
                    }
                    catch { }
                    return ValueTask.CompletedTask;
                });
            result = await repository.GetIdentitiesByHashesAsync([.. matchingHashes], page, pageSize, sortBy, sortDir);
        }
        else
        {
            result = await repository.GetIdentitiesAsync(page, pageSize, sortBy, sortDir, search);
        }

        await DecryptIdentityEmailsAsync(result.Records, repository, encryptor, ct);

        return Results.Ok(new
        {
            records = result.Records.Select(r => new
            {
                emailHash = r.EmailHash,
                email = r.Email,
                name = r.Name,
                bucketCount = r.BucketCount,
                firstSeen = r.FirstSeen,
                lastChanged = r.LastChanged
            }),
            total = result.Total,
            page = result.Page,
            pageSize = result.PageSize
        });
    }

    private static async Task<PagedResult<IdentityInfo>> GetIdentitiesSortedByEmailAsync(
        int page, int pageSize, string? sortDir, string? search, string? searchType,
        IConsentRepository repository, Encryptor encryptor, CancellationToken ct)
    {
        var mappings = await repository.GetEmailHashMappingsAsync();

        var decrypted = new System.Collections.Concurrent.ConcurrentBag<(string Hash, string? Email)>();
        await Parallel.ForEachAsync(mappings,
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = ct },
            (mapping, token) =>
            {
                string? email = null;
                if (mapping.EncryptedEmail != null)
                    try { email = encryptor.Decrypt(mapping.EncryptedEmail); } catch { }
                decrypted.Add((mapping.EmailHash, email));
                return ValueTask.CompletedTask;
            });

        IEnumerable<(string Hash, string? Email)> filtered = decrypted;
        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchLower = search.Trim().ToLowerInvariant();
            filtered = searchType == "email"
                ? decrypted.Where(x => MatchesWildcard(x.Email, searchLower))
                : decrypted.Where(x => MatchesWildcard(x.Hash, searchLower));
        }

        var sorted = (sortDir?.ToLowerInvariant() == "asc"
            ? filtered.OrderBy(x => x.Email ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            : filtered.OrderByDescending(x => x.Email ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            .ToList();

        var total = sorted.Count;
        var pageHashes = sorted.Skip((page - 1) * pageSize).Take(pageSize).Select(x => x.Hash).ToList();

        if (pageHashes.Count == 0)
            return new PagedResult<IdentityInfo> { Records = [], Total = total, Page = page, PageSize = pageSize };

        var pageResult = await repository.GetIdentitiesByHashesAsync(pageHashes, 1, pageHashes.Count, null, null);

        var emailByHash = sorted
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToDictionary(x => x.Hash, x => x.Email);
        foreach (var record in pageResult.Records)
        {
            if (emailByHash.TryGetValue(record.EmailHash, out var email))
                record.Email = email;
        }

        // Re-order to match the email-sorted page order
        var ordered = pageHashes
            .Select(h => pageResult.Records.FirstOrDefault(r => r.EmailHash == h))
            .OfType<IdentityInfo>()
            .ToList();

        return new PagedResult<IdentityInfo> { Records = ordered, Total = total, Page = page, PageSize = pageSize };
    }

    private static async Task DecryptIdentityEmailsAsync(
        IReadOnlyList<IdentityInfo> records,
        IConsentRepository repository,
        Encryptor encryptor,
        CancellationToken ct)
    {
        if (records.Count == 0) return;
        var hashes = records.Select(r => r.EmailHash).ToList();
        var contacts = await repository.GetEncryptedEmailsForHashesAsync(hashes, ct);
        foreach (var record in records)
        {
            if (!contacts.TryGetValue(record.EmailHash, out var contact)) continue;
            if (contact.EncryptedEmail != null)
                try { record.Email = encryptor.Decrypt(contact.EncryptedEmail); } catch { }
            if (contact.EncryptedName != null)
                try { record.Name = encryptor.Decrypt(contact.EncryptedName); } catch { }
        }
    }

    private static async Task<IResult> ExportIdentities(
        [FromBody] ExportIdentitiesRequest? request,
        [FromServices] IConsentRepository repository,
        [FromServices] Encryptor encryptor,
        HttpContext context,
        CancellationToken ct)
    {
        var format = (request?.Format ?? "csv").ToLowerInvariant();
        if (format is not ("csv" or "json"))
            return Results.BadRequest(new { error = "Invalid format. Must be 'csv' or 'json'" });

        PagedResult<IdentityInfo> result;

        if (request?.Hashes is { Count: > 0 })
        {
            if (request.Hashes.Count > 10_000)
                return Results.BadRequest(new { error = "Too many hashes selected. Maximum is 10,000." });

            foreach (var h in request.Hashes)
            {
                var v = InputValidator.ValidateEmailHash(h);
                if (!v.IsValid) return Results.BadRequest(new { error = $"Invalid hash: {v.Error}" });
            }

            result = await repository.GetIdentitiesByHashesAsync(request.Hashes, 1, request.Hashes.Count, "lastchanged", "desc");
        }
        else
        {
            // Export all (capped at 10k for safety)
            result = await repository.GetIdentitiesAsync(1, 10_000, "lastchanged", "desc");
        }

        var records = result.Records.ToList();

        if (records.Count > 0)
        {
            var hashes = records.Select(r => r.EmailHash).ToList();
            var contacts = await repository.GetEncryptedEmailsForHashesAsync(hashes, ct);
            await Parallel.ForEachAsync(records,
                new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = ct },
                (record, token) =>
                {
                    if (contacts.TryGetValue(record.EmailHash, out var c))
                    {
                        if (c.EncryptedEmail != null) try { record.Email = encryptor.Decrypt(c.EncryptedEmail); } catch { }
                        if (c.EncryptedName != null) try { record.Name = encryptor.Decrypt(c.EncryptedName); } catch { }
                    }
                    return ValueTask.CompletedTask;
                });
        }

        var filename = $"subscribers-{DateTime.UtcNow:yyyyMMdd}.{format}";
        context.Response.Headers["Content-Disposition"] = $"attachment; filename=\"{filename}\"";

        if (format == "json")
            return Results.Json(records.Select(r => new
            {
                email = r.Email,
                name = r.Name,
                beaconId = r.EmailHash,
                buckets = r.BucketCount,
                created = r.FirstSeen.ToString("O"),
                updated = r.LastChanged.ToString("O")
            }));

        var sb = new StringBuilder();
        sb.AppendLine("Email,Name,Beacon ID,Buckets,Created,Updated");
        foreach (var r in records)
            sb.AppendLine($"{CsvEscape(r.Email ?? "")},{CsvEscape(r.Name ?? "")},{CsvEscape(r.EmailHash)},{r.BucketCount},{r.FirstSeen:O},{r.LastChanged:O}");

        return Results.Content(sb.ToString(), "text/csv");
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

        string? name = null;
        if (!string.IsNullOrEmpty(details.EncryptedName))
        {
            try { name = encryptor.Decrypt(details.EncryptedName); }
            catch { }
        }

        return Results.Ok(new
        {
            details.EmailHash,
            Email = email,
            Name = name,
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
        if (config.LoginFooter != null && config.LoginFooter.Length > 500)
            return Results.BadRequest(new { error = "Login footer must be 500 characters or less." });
        if (config.PromoBar != null && config.PromoBar.Length > 500)
            return Results.BadRequest(new { error = "Announcement bar must be 500 characters or less." });

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
        [FromServices] HostRoutingOptions routingOptions,
        HttpContext context,
        CancellationToken ct)
    {
        var normalized = bucket.Trim().ToLowerInvariant();
        var records = await repository.GetAllBucketRecordsAsync(normalized);

        var baseUrl = !string.IsNullOrEmpty(instanceOptions.PublicUrl)
            ? instanceOptions.PublicUrl.TrimEnd('/')
            : $"{context.Request.Scheme}://{context.Request.Host.Host}:{routingOptions.ApiPort}";

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

    private static bool MatchesWildcard(string? text, string pattern)
    {
        if (text is null) return false;
        if (!pattern.Contains('*'))
            return text.Contains(pattern, StringComparison.OrdinalIgnoreCase);
        var regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$";
        return Regex.IsMatch(text, regexPattern, RegexOptions.IgnoreCase);
    }

    internal sealed record ExportIdentitiesRequest(
        IReadOnlyList<string>? Hashes = null,
        string Format = "csv");

    internal sealed record ExportBucketRecordsRequest(
        IReadOnlyList<string>? Hashes = null,
        string Format = "csv",
        bool IncludeTracking = false,
        string? Permission = null,
        string? UtmCampaign = null,
        string? UtmSource = null,
        string? UtmMedium = null,
        int ExpiryDays = 30,
        string Language = "en");

    private static async Task<IResult> ExportBucketRecords(
        string bucket,
        [FromBody] ExportBucketRecordsRequest? request,
        [FromServices] IConsentRepository repository,
        [FromServices] Encryptor encryptor,
        [FromServices] TokenGenerator generator,
        [FromServices] Beacon.Core.Services.InstanceOptions instanceOptions,
        [FromServices] HostRoutingOptions routingOptions,
        HttpContext context,
        CancellationToken ct)
    {
        var normalized = bucket.Trim().ToLowerInvariant();
        var bucketVal = InputValidator.ValidateBucket(normalized);
        if (!bucketVal.IsValid) return Results.BadRequest(new { error = bucketVal.Error });

        var format = (request?.Format ?? "csv").ToLowerInvariant();
        if (format is not ("csv" or "json" or "xml" or "yaml"))
            return Results.BadRequest(new { error = "Invalid format. Must be 'csv', 'json', 'xml', or 'yaml'" });

        if (request?.Hashes is { Count: > 10_000 })
            return Results.BadRequest(new { error = "Too many hashes selected. Maximum is 10,000." });

        if (request?.Hashes != null)
            foreach (var h in request.Hashes)
            {
                var v = InputValidator.ValidateEmailHash(h);
                if (!v.IsValid) return Results.BadRequest(new { error = $"Invalid hash: {v.Error}" });
            }

        var allRecords = await repository.GetAllBucketRecordsAsync(normalized);

        IEnumerable<EmailPermissions> filtered = request?.Hashes is { Count: > 0 }
            ? allRecords.Where(r => request.Hashes.Contains(r.EmailHash))
            : allRecords;

        var records = filtered.ToList();
        var permissions = records
            .SelectMany(r => r.Permissions.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p)
            .ToList();

        var customFieldKeys = records
            .SelectMany(r => r.CustomFields?.Keys ?? (IEnumerable<string>)[])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k)
            .ToList();

        await Parallel.ForEachAsync(records,
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = ct },
            (record, _) =>
            {
                if (!string.IsNullOrEmpty(record.EncryptedEmail))
                    try { record.Email = encryptor.Decrypt(record.EncryptedEmail); } catch { }
                if (!string.IsNullOrEmpty(record.EncryptedName))
                    try { record.Name = encryptor.Decrypt(record.EncryptedName); } catch { }
                return ValueTask.CompletedTask;
            });

        Dictionary<string, (string Url, DateTime? ExpiresAt)>? trackingByHash = null;
        if (request?.IncludeTracking == true)
        {
            var baseUrl = !string.IsNullOrEmpty(instanceOptions.PublicUrl)
                ? instanceOptions.PublicUrl.TrimEnd('/')
                : $"{context.Request.Scheme}://{context.Request.Host.Host}:{routingOptions.ApiPort}";

            var utmParts = new List<string>(3);
            if (!string.IsNullOrWhiteSpace(request.UtmCampaign))
                utmParts.Add($"utm_campaign={Uri.EscapeDataString(request.UtmCampaign)}");
            if (!string.IsNullOrWhiteSpace(request.UtmSource))
                utmParts.Add($"utm_source={Uri.EscapeDataString(request.UtmSource)}");
            if (!string.IsNullOrWhiteSpace(request.UtmMedium))
                utmParts.Add($"utm_medium={Uri.EscapeDataString(request.UtmMedium)}");
            var qs = utmParts.Count > 0 ? "?" + string.Join("&", utmParts) : string.Empty;

            var tokenOpts = new Tokens.GenerateTokenRequest
            {
                AllowReplay = true,
                ExpiryDays = request.ExpiryDays > 0 ? request.ExpiryDays : 30,
                Language = string.IsNullOrWhiteSpace(request.Language) ? "en" : request.Language
            };

            trackingByHash = new Dictionary<string, (string, DateTime?)>(records.Count);
            foreach (var rec in records)
            {
                if (string.IsNullOrEmpty(rec.Email)) continue;
                string[] permNames;
                if (!string.IsNullOrEmpty(request.Permission))
                {
                    if (!rec.Permissions.ContainsKey(request.Permission)) continue;
                    permNames = [request.Permission];
                }
                else
                {
                    permNames = [.. rec.Permissions.Keys];
                }
                if (permNames.Length == 0) continue;
                var tok = generator.Generate(normalized, rec.Email, permNames, tokenOpts);
                trackingByHash[rec.EmailHash] = ($"{baseUrl}/u/{tok}{qs}", TryDecodeTokenExpiry(tok));
            }
        }

        var filename = $"records-{normalized}-{DateTime.UtcNow:yyyyMMdd}.{format}";
        context.Response.Headers["Content-Disposition"] = $"attachment; filename=\"{filename}\"";

        return format switch
        {
            "json" => Results.Json(BuildRecordsJson(records, permissions, normalized, trackingByHash)),
            "xml"  => Results.Content(BuildRecordsXml(records, permissions, normalized, trackingByHash), "application/xml"),
            "yaml" => Results.Content(BuildRecordsYaml(records, permissions, normalized, trackingByHash), "text/yaml"),
            _      => Results.Content(BuildRecordsCsv(records, permissions, customFieldKeys, normalized, trackingByHash), "text/csv")
        };
    }

    private static string ToCamelCase(string name)
    {
        var parts = name.Split('_');
        if (parts.Length == 1) return parts[0];
        return parts[0] + string.Concat(parts[1..].Select(p => p.Length > 0 ? char.ToUpperInvariant(p[0]) + p[1..] : ""));
    }

    private static string ToPascalCase(string name) =>
        string.Concat(name.Split('_').Select(p => p.Length > 0 ? char.ToUpperInvariant(p[0]) + p[1..] : ""));

    private static string YamlEscape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "\"\"";
        if (value.IndexOfAny(['"', ':', '\n', '\r', '#', '[', ']', '{', '}', '&', '*', '?', '|', '<', '>', '=', '!', '%', '@', '`', '\\']) >= 0
            || value.StartsWith(' ') || value.EndsWith(' '))
            return $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
        return value;
    }

    private static DateTime? TryDecodeTokenExpiry(string token)
    {
        var parts = token.Split('.');
        if (parts.Length < 2) return null;
        try
        {
            var padded = parts[1].Replace('-', '+').Replace('_', '/');
            padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
            using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(padded)));
            if (doc.RootElement.TryGetProperty("exp", out var expEl) && expEl.TryGetInt64(out var exp))
                return DateTimeOffset.FromUnixTimeSeconds(exp).UtcDateTime;
        }
        catch { }
        return null;
    }

    private static IEnumerable<Dictionary<string, object?>> BuildRecordsJson(
        List<EmailPermissions> records,
        List<string> permissions,
        string bucket,
        Dictionary<string, (string Url, DateTime? ExpiresAt)>? tracking)
    {
        return records.Select(r =>
        {
            var consent = permissions.ToDictionary(
                p => ToCamelCase(p),
                p => (object?)(r.Permissions.TryGetValue(p, out var v) ? v : false));
            var obj = new Dictionary<string, object?>(8)
            {
                ["beaconId"]    = r.EmailHash,
                ["email"]       = r.Email,
                ["fullName"]    = r.Name,
                ["consent"]     = consent,
                ["createdDate"] = r.FirstSeen.ToString("O"),
                ["updatedDate"] = r.LastChanged.ToString("O")
            };
            if (tracking != null && tracking.TryGetValue(r.EmailHash, out var t))
                obj["tracking"] = new Dictionary<string, object?>
                {
                    ["campaign"]  = bucket,
                    ["expiresAt"] = t.ExpiresAt?.ToString("O"),
                    ["url"]       = t.Url
                };
            return obj;
        });
    }

    private static string BuildRecordsCsv(
        List<EmailPermissions> records,
        List<string> permissions,
        List<string> customFieldKeys,
        string bucket,
        Dictionary<string, (string Url, DateTime? ExpiresAt)>? tracking)
    {
        var sb = new StringBuilder();
        var cfHeaders  = customFieldKeys.Count > 0 ? string.Join(",", customFieldKeys.Select(CsvEscape)) + "," : "";
        var permHeaders = string.Join(",", permissions.Select(CsvEscape));
        var trackHeaders = tracking != null ? ",campaignId,trackingUrl" : "";
        sb.AppendLine($"email,fullName,beaconId,{cfHeaders}{permHeaders},createdDate,updatedDate{trackHeaders}");
        foreach (var r in records)
        {
            var cfVals   = customFieldKeys.Count > 0
                ? string.Join(",", customFieldKeys.Select(k => CsvEscape(r.CustomFields?.TryGetValue(k, out var v) == true ? v ?? "" : ""))) + ","
                : "";
            var permVals = string.Join(",", permissions.Select(p => r.Permissions.TryGetValue(p, out var v) ? (v ? "true" : "false") : "false"));
            var trackVals = tracking != null
                ? "," + CsvEscape(bucket) + "," + (tracking.TryGetValue(r.EmailHash, out var t) ? CsvEscape(t.Url) : "")
                : "";
            sb.AppendLine($"{CsvEscape(r.Email ?? "")},{CsvEscape(r.Name ?? "")},{CsvEscape(r.EmailHash)},{cfVals}{permVals},{r.FirstSeen:O},{r.LastChanged:O}{trackVals}");
        }
        return sb.ToString();
    }

    private static string BuildRecordsXml(
        List<EmailPermissions> records,
        List<string> permissions,
        string bucket,
        Dictionary<string, (string Url, DateTime? ExpiresAt)>? tracking)
    {
        var doc = new System.Xml.Linq.XDocument(
            new System.Xml.Linq.XElement("Export",
                records.Select(r =>
                {
                    var el = new System.Xml.Linq.XElement("ConsentRecord",
                        new System.Xml.Linq.XElement("BeaconId", r.EmailHash),
                        new System.Xml.Linq.XElement("Email", r.Email ?? ""),
                        new System.Xml.Linq.XElement("FullName", r.Name ?? ""),
                        new System.Xml.Linq.XElement("Consent",
                            permissions.Select(p => new System.Xml.Linq.XElement(
                                ToPascalCase(p),
                                r.Permissions.TryGetValue(p, out var v) && v ? "true" : "false"))),
                        new System.Xml.Linq.XElement("CreatedDate", r.FirstSeen.ToString("O")),
                        new System.Xml.Linq.XElement("UpdatedDate", r.LastChanged.ToString("O")));
                    if (tracking != null && tracking.TryGetValue(r.EmailHash, out var t))
                        el.Add(new System.Xml.Linq.XElement("Tracking",
                            new System.Xml.Linq.XElement("Campaign", bucket),
                            new System.Xml.Linq.XElement("ExpiresAt", t.ExpiresAt?.ToString("O") ?? ""),
                            new System.Xml.Linq.XElement("Url", t.Url)));
                    return el;
                })));
        return doc.ToString();
    }

    private static string BuildRecordsYaml(
        List<EmailPermissions> records,
        List<string> permissions,
        string bucket,
        Dictionary<string, (string Url, DateTime? ExpiresAt)>? tracking)
    {
        var sb = new StringBuilder();
        sb.AppendLine("subscribers:");
        foreach (var r in records)
        {
            sb.AppendLine($"  - beaconId: {YamlEscape(r.EmailHash)}");
            sb.AppendLine($"    email: {YamlEscape(r.Email)}");
            sb.AppendLine($"    fullName: {YamlEscape(r.Name)}");
            if (permissions.Count > 0)
            {
                sb.AppendLine("    consent:");
                foreach (var p in permissions)
                    sb.AppendLine($"      {ToCamelCase(p)}: {(r.Permissions.TryGetValue(p, out var v) && v ? "true" : "false")}");
            }
            sb.AppendLine($"    createdDate: \"{r.FirstSeen:O}\"");
            sb.AppendLine($"    updatedDate: \"{r.LastChanged:O}\"");
            if (tracking != null && tracking.TryGetValue(r.EmailHash, out var t))
            {
                sb.AppendLine("    tracking:");
                sb.AppendLine($"      campaign: {YamlEscape(bucket)}");
                sb.AppendLine($"      expiresAt: \"{t.ExpiresAt?.ToString("O") ?? ""}\"");
                sb.AppendLine($"      url: {YamlEscape(t.Url)}");
            }
        }
        return sb.ToString();
    }

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

    private static readonly Regex PartialHashPattern = new(@"^[a-fA-F0-9]{1,64}$", RegexOptions.Compiled);

    private static async Task<IResult> GetAuditLog(
        [FromQuery] string? bucket,
        [FromQuery] string? emailHash,
        [FromQuery] string? emailSearch,
        [FromQuery] int page = 1,
        [FromQuery] int size = 25,
        [FromServices] IConsentRepository repository = null!,
        [FromServices] Encryptor encryptor = null!,
        CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        size = Math.Clamp(size, 1, 100);

        if (!string.IsNullOrWhiteSpace(emailHash) && !PartialHashPattern.IsMatch(emailHash.Trim()))
            return Results.BadRequest(new { error = "Invalid emailHash format" });
        if (!string.IsNullOrWhiteSpace(bucket))
        {
            var bucketVal = InputValidator.ValidateBucket(bucket.Trim());
            if (!bucketVal.IsValid) return Results.BadRequest(new { error = bucketVal.Error });
        }

        var normalizedBucket = string.IsNullOrWhiteSpace(bucket) ? null : bucket.Trim().ToLowerInvariant();
        string? normalizedHash = null;

        IReadOnlyList<string>? matchedHashes = null;
        if (!string.IsNullOrWhiteSpace(emailSearch))
        {
            // Resolve email pattern to matching hashes (supports * wildcards)
            var searchLower = emailSearch.Trim().ToLowerInvariant();
            var mappings = await repository.GetEmailHashMappingsAsync();
            var found = new List<string>();
            foreach (var m in mappings)
            {
                if (m.EncryptedEmail is null) continue;
                try
                {
                    if (MatchesWildcard(encryptor.Decrypt(m.EncryptedEmail), searchLower))
                        found.Add(m.EmailHash);
                }
                catch { }
            }
            if (found.Count == 0)
                return Results.Ok(new { records = Array.Empty<object>(), total = 0, page, pageSize = size });
            matchedHashes = found;
        }
        else
        {
            normalizedHash = string.IsNullOrWhiteSpace(emailHash) ? null : emailHash.Trim().ToLowerInvariant();
        }

        var result = await repository.GetAuditAsync(normalizedBucket, normalizedHash, page, size, ct, matchedHashes);

        // Decrypt emails for audit records
        var distinctHashes = result.Records.Select(e => e.EmailHash).Distinct().ToList();
        var contactMap = distinctHashes.Count > 0
            ? await repository.GetEncryptedEmailsForHashesAsync(distinctHashes, ct)
            : new Dictionary<string, EncryptedContact>();

        string? DecryptEmail(string hash)
        {
            if (!contactMap.TryGetValue(hash, out var c) || c.EncryptedEmail is null) return null;
            try { return encryptor.Decrypt(c.EncryptedEmail); } catch { return null; }
        }

        string? DecryptName(string hash)
        {
            if (!contactMap.TryGetValue(hash, out var c) || c.EncryptedName is null) return null;
            try { return encryptor.Decrypt(c.EncryptedName); } catch { return null; }
        }

        return Results.Ok(new
        {
            records = result.Records.Select(e => new
            {
                id = e.Id,
                bucket = e.Bucket,
                emailHash = e.EmailHash,
                email = DecryptEmail(e.EmailHash),
                name = DecryptName(e.EmailHash),
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

    // Brand Identities

    private static IResult GetBrandIdentities([FromServices] IBrandIdentityService brandService)
    {
        var identities = brandService.GetAll().Select(i => new
        {
            id = i.Id,
            name = i.Name,
            isDefault = i.IsDefault,
            settings = JsonSerializer.Deserialize<object>(i.Settings) ?? new { },
            buckets = i.BucketMappings.Select(b => b.Bucket).ToList(),
            updatedAt = i.UpdatedAt
        });
        return Results.Ok(identities);
    }

    private static async Task<IResult> CreateBrandIdentity(
        [FromBody] BrandIdentityCreateRequest request,
        [FromServices] IBrandIdentityService brandService,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 100)
            return Results.BadRequest(new { error = "Name is required and must be 100 characters or fewer." });

        var identity = await brandService.CreateAsync(request.Name.Trim(), ct);
        return Results.Ok(new { id = identity.Id, name = identity.Name, isDefault = identity.IsDefault });
    }

    private static async Task<IResult> UpdateBrandIdentity(
        int id,
        [FromBody] BrandIdentityUpdateRequest request,
        [FromServices] IBrandIdentityService brandService,
        CancellationToken ct)
    {
        var existing = brandService.GetById(id);
        if (existing is null)
            return Results.NotFound(new { error = "Brand identity not found." });

        if (existing.IsDefault && request.Name.Trim() != existing.Name)
            return Results.BadRequest(new { error = "The Default identity cannot be renamed." });

        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 100)
            return Results.BadRequest(new { error = "Name is required and must be 100 characters or fewer." });

        string settingsJson;
        BrandIdentitySettings parsedSettings;
        if (request.Settings is JsonElement je)
        {
            parsedSettings = JsonSerializer.Deserialize<BrandIdentitySettings>(je.GetRawText())
                ?? new BrandIdentitySettings();
            if (existing.IsDefault)
                parsedSettings = parsedSettings with { PrimaryAccent = null, SurfaceColour = null };
            settingsJson = JsonSerializer.Serialize(parsedSettings);
        }
        else
        {
            parsedSettings = new BrandIdentitySettings();
            settingsJson = "{}";
        }

        var settingsError = ValidateIdentitySettings(parsedSettings);
        if (settingsError is not null)
            return Results.BadRequest(new { error = settingsError });

        var updated = await brandService.UpdateAsync(id, request.Name.Trim(), settingsJson, ct);
        return Results.Ok(new { id = updated.Id, name = updated.Name, updatedAt = updated.UpdatedAt });
    }

    private static async Task<IResult> DeleteBrandIdentity(
        int id,
        [FromServices] IBrandIdentityService brandService,
        CancellationToken ct)
    {
        if (brandService.GetById(id) is null)
            return Results.NotFound(new { error = "Brand identity not found." });

        try
        {
            await brandService.DeleteAsync(id, ct);
            return Results.Ok(new { message = "Brand identity deleted." });
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> AssignBrandIdentityBuckets(
        int id,
        [FromBody] BrandIdentityBucketsRequest request,
        [FromServices] IBrandIdentityService brandService,
        CancellationToken ct)
    {
        if (brandService.GetById(id) is null)
            return Results.NotFound(new { error = "Brand identity not found." });

        var cleaned = (request.Buckets ?? [])
            .Select(b => b?.Trim() ?? "")
            .Where(b => b.Length > 0)
            .Distinct()
            .ToList();

        if (cleaned.Any(b => b.Length > 100))
            return Results.BadRequest(new { error = "Bucket names must be 100 characters or fewer." });

        if (cleaned.Count > 200)
            return Results.BadRequest(new { error = "Cannot assign more than 200 buckets to one identity." });

        await brandService.AssignBucketsAsync(id, cleaned, ct);
        return Results.Ok(new { message = "Buckets assigned." });
    }

    private static string? ValidateIdentitySettings(BrandIdentitySettings s)
    {
        if (s.Theme is not null && !_allowedThemes.Contains(s.Theme))
            return "theme must be 'system', 'light', or 'dark'.";

        if (s.PrimaryAccent is not null && !_hexColour.IsMatch(s.PrimaryAccent))
            return "primaryAccent must be a 6-digit hex colour (e.g. #6366f1).";

        if (s.SurfaceColour is not null && !_hexColour.IsMatch(s.SurfaceColour))
            return "surfaceColour must be a 6-digit hex colour (e.g. #ffffff).";

        if (s.Font is not null && !_allowedFonts.Contains(s.Font))
            return $"font must be one of: {string.Join(", ", _allowedFonts)}.";

        if (s.PageTitle?.Length > 200)    return "pageTitle must be 200 characters or fewer.";
        if (s.BrowserTitle?.Length > 200) return "browserTitle must be 200 characters or fewer.";
        if (s.EmailTitle?.Length > 200)   return "emailTitle must be 200 characters or fewer.";
        if (s.ConfirmTitle?.Length > 200) return "confirmTitle must be 200 characters or fewer.";
        if (s.PageBody?.Length > 1000)    return "pageBody must be 1000 characters or fewer.";
        if (s.EmailBody?.Length > 1000)   return "emailBody must be 1000 characters or fewer.";
        if (s.ConfirmMsg?.Length > 1000)  return "confirmMsg must be 1000 characters or fewer.";
        if (s.Footer?.Length > 500)       return "footer must be 500 characters or fewer.";

        if (s.Logo is not null)
        {
            if (!_allowedLogoTypes.Contains(s.Logo.Type))
                return "logo.type must be 'base64', 'objectStorage', or 'url'.";

            if (s.Logo.Type is "url" or "objectStorage" && string.IsNullOrWhiteSpace(s.Logo.Url))
                return $"logo.url is required when logo type is '{s.Logo.Type}'.";

            if (s.Logo.Type == "base64")
            {
                if (string.IsNullOrWhiteSpace(s.Logo.Data))
                    return "logo.data is required when logo type is 'base64'.";
                if (!s.Logo.Data.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
                    return "logo.data must be an image data URI.";
            }
        }

        return null;
    }

    private static async Task<IResult> UploadLogo(
        HttpContext context,
        [FromServices] ISystemConfigurationService configService,
        [FromServices] IObjectStorageService objectStorage)
    {
        if (!context.Request.HasFormContentType)
            return Results.BadRequest(new { error = "Multipart form required." });

        var form = await context.Request.ReadFormAsync();
        var file = form.Files.GetFile("file");
        var base64Data = form["base64"].FirstOrDefault();

        const long maxBytes = 2 * 1024 * 1024;

        // Object storage path
        var sysConfig = configService.Get();
        if (sysConfig.ObjectStorage && file is not null)
        {
            if (file.Length > maxBytes)
                return Results.BadRequest(new { error = "File exceeds 2 MB limit." });

            if (!file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(new { error = "Only image files are accepted." });

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            var key = $"logos/{Guid.NewGuid():N}{ext}";
            using var stream = file.OpenReadStream();
            var url = await objectStorage.UploadAsync(key, stream, file.ContentType);
            return Results.Ok(new { type = "objectStorage", url });
        }

        // Base64 path (no object storage configured)
        if (!string.IsNullOrEmpty(base64Data))
        {
            if (base64Data.Length > maxBytes * 2)
                return Results.BadRequest(new { error = "Image data exceeds 2 MB limit." });

            if (!base64Data.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(new { error = "Only image data URIs are accepted." });

            return Results.Ok(new { type = "base64", data = base64Data });
        }

        return Results.BadRequest(new { error = "Provide a file (with object storage) or a base64 data URI." });
    }
}

public sealed class OverrideConsentRequest
{
    public string Bucket { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Permission { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Name { get; set; }
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
    /// Optional name to store alongside the email record. Encrypted at rest.
    /// </summary>
    public string? Name { get; set; }

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
    public string? Name { get; set; }
    /// <summary>
    /// Permission states: {"newsletter": "OptedIn", "marketing": "OptedOut"}
    /// </summary>
    public Dictionary<string, string> Permissions { get; set; } = [];
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

public sealed class BrandIdentityCreateRequest
{
    public string Name { get; set; } = string.Empty;
}

public sealed class BrandIdentityUpdateRequest
{
    public string Name { get; set; } = string.Empty;
    public object? Settings { get; set; }
}

public sealed class BrandIdentityBucketsRequest
{
    public List<string>? Buckets { get; set; }
}
