using System.Text.Json.Serialization;

namespace Beacon.Core.Services;

public sealed class SystemConfig
{
    [JsonPropertyName("allowDbLookup")]
    public bool AllowDbLookup { get; set; } = false;

    [JsonPropertyName("defaultLanguage")]
    public string DefaultLanguage { get; set; } = "en";

    // Integration — Email
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

    // Integration — Object storage
    [JsonPropertyName("objectStorage")]
    public bool ObjectStorage { get; set; } = false;

    // Modules
    [JsonPropertyName("enableDoubleOptIn")]
    public bool EnableDoubleOptIn { get; set; } = false;

    [JsonPropertyName("perPermissionEmail")]
    public bool PerPermissionEmail { get; set; } = false;

    // System
    [JsonPropertyName("emailQueueCron")]
    public string EmailQueueCron { get; set; } = "*/5 * * * *";

    [JsonPropertyName("emailQueueRetentionDays")]
    public int EmailQueueRetentionDays { get; set; } = 90;
}

public interface ISystemConfigurationService
{
    SystemConfig Get();
    Task SaveAsync(SystemConfig config);
}
