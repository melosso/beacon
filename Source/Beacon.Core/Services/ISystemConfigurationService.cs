using System.Text.Json.Serialization;

namespace Beacon.Core.Services;

public sealed class SystemConfig
{
    [JsonPropertyName("allowDbLookup")]
    public bool AllowDbLookup { get; set; } = true;

    [JsonPropertyName("defaultLanguage")]
    public string DefaultLanguage { get; set; } = "en";

    // Integration / Email
    [JsonPropertyName("emailNotifications")]
    public bool EmailNotifications { get; set; } = false;

    [JsonPropertyName("emailProvider")]
    public string EmailProvider { get; set; } = "none";

    [JsonPropertyName("emailResendApiKey")]
    public string EmailResendApiKey { get; set; } = string.Empty;

    [JsonPropertyName("emailFromAddress")]
    public string EmailFromAddress { get; set; } = string.Empty;

    [JsonPropertyName("emailFromName")]
    public string EmailFromName { get; set; } = string.Empty;

    [JsonPropertyName("emailSmtpHost")]
    public string EmailSmtpHost { get; set; } = string.Empty;

    [JsonPropertyName("emailSmtpPort")]
    public int EmailSmtpPort { get; set; } = 587;

    [JsonPropertyName("emailSmtpUsername")]
    public string EmailSmtpUsername { get; set; } = string.Empty;

    [JsonPropertyName("emailSmtpPassword")]
    public string EmailSmtpPassword { get; set; } = string.Empty;

    [JsonPropertyName("emailSmtpUseTls")]
    public bool EmailSmtpUseTls { get; set; } = true;

    [JsonPropertyName("emailQueueEnabled")]
    public bool EmailQueueEnabled { get; set; } = false;

    // Integration / Object storage
    [JsonPropertyName("objectStorage")]
    public bool ObjectStorage { get; set; } = false;

    [JsonPropertyName("objectStorageProvider")]
    public string ObjectStorageProvider { get; set; } = "none"; // "s3" | "r2" | "minio"

    [JsonPropertyName("objectStorageBucket")]
    public string ObjectStorageBucket { get; set; } = string.Empty;

    [JsonPropertyName("objectStorageRegion")]
    public string ObjectStorageRegion { get; set; } = "us-east-1";

    [JsonPropertyName("objectStorageEndpoint")]
    public string ObjectStorageEndpoint { get; set; } = string.Empty;

    [JsonPropertyName("objectStorageAccessKey")]
    public string ObjectStorageAccessKey { get; set; } = string.Empty;

    [JsonPropertyName("objectStorageSecretKey")]
    public string ObjectStorageSecretKey { get; set; } = string.Empty;

    [JsonPropertyName("objectStoragePublicUrl")]
    public string ObjectStoragePublicUrl { get; set; } = string.Empty;

    // Modules
    [JsonPropertyName("enableSubmissionForms")]
    public bool EnableSubmissionForms { get; set; } = true;

    [JsonPropertyName("submissionDefaultRateLimitPerMinute")]
    public int SubmissionDefaultRateLimitPerMinute { get; set; } = 10;

    [JsonPropertyName("submissionDefaultHoneypotEnabled")]
    public bool SubmissionDefaultHoneypotEnabled { get; set; } = true;

    [JsonPropertyName("submissionDefaultConsentRequired")]
    public bool SubmissionDefaultConsentRequired { get; set; } = true;

    [JsonPropertyName("enableDoubleOptIn")]
    public bool EnableDoubleOptIn { get; set; } = false;

    [JsonPropertyName("perPermissionEmail")]
    public bool PerPermissionEmail { get; set; } = false;

    [JsonPropertyName("enableUtmTracking")]
    public bool EnableUtmTracking { get; set; } = false;

    // System
    [JsonPropertyName("emailQueueCron")]
    public string EmailQueueCron { get; set; } = "*/5 * * * *";

    [JsonPropertyName("emailQueueRetentionDays")]
    public int EmailQueueRetentionDays { get; set; } = 90;

    // Data Policies
    [JsonPropertyName("dataPoliciesEnabled")]
    public bool DataPoliciesEnabled { get; set; }

    [JsonPropertyName("dataPolicyCron")]
    public string DataPolicyCron { get; set; } = "0 0 * * *";

    [JsonPropertyName("retentionPurgeEnabled")]
    public bool RetentionPurgeEnabled { get; set; }

    [JsonPropertyName("retentionPurgeDays")]
    public int RetentionPurgeDays { get; set; } = 1095;

    [JsonPropertyName("pendingConfirmationPurgeEnabled")]
    public bool PendingConfirmationPurgeEnabled { get; set; }

    [JsonPropertyName("pendingConfirmationPurgeDays")]
    public int PendingConfirmationPurgeDays { get; set; } = 30;

    [JsonPropertyName("retentionPurgeRequireApproval")]
    public bool RetentionPurgeRequireApproval { get; set; }

    [JsonPropertyName("pendingConfirmationPurgeRequireApproval")]
    public bool PendingConfirmationPurgeRequireApproval { get; set; }

    // Performance / Caching
    [JsonPropertyName("enableCaching")]
    public bool EnableCaching { get; set; } = false;

    [JsonPropertyName("cacheTtlSeconds")]
    public int CacheTtlSeconds { get; set; } = 300;

    [JsonPropertyName("cacheConsentRecords")]
    public bool CacheConsentRecords { get; set; } = true;

    [JsonPropertyName("cacheBucketData")]
    public bool CacheBucketData { get; set; } = true;

    // Server metadata (read-only, populated by the endpoint, not persisted)
    [JsonPropertyName("version")]
    public string? Version { get; set; }

    // Appearance / Branding
    [JsonPropertyName("loginFooterEnabled")]
    public bool LoginFooterEnabled { get; set; } = false;

    [JsonPropertyName("loginFooter")]
    public string LoginFooter { get; set; } = string.Empty;

    [JsonPropertyName("promoBarEnabled")]
    public bool PromoBarEnabled { get; set; } = false;

    [JsonPropertyName("promoBar")]
    public string PromoBar { get; set; } = string.Empty;

    [JsonPropertyName("promoBarDismissable")]
    public bool PromoBarDismissable { get; set; } = true;

    [JsonPropertyName("promoBarShowOnLogin")]
    public bool PromoBarShowOnLogin { get; set; } = false;
}

public interface ISystemConfigurationService
{
    SystemConfig Get();
    Task SaveAsync(SystemConfig config);
}
