namespace Beacon.Core.Models;

public sealed class SubmissionForm
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Bucket { get; set; }
    public required string Permission { get; set; }
    public required string AllowedOrigins { get; set; } // JSON array: ["https://example.com"]
    public string? FormConfig { get; set; } // JSON: {title, description, buttonText, successMessage, primaryColor, ...}
    public required string EncryptedApiToken { get; set; }
    public int RateLimitPerMinute { get; set; } = 10;
    public bool HoneypotEnabled { get; set; } = true;
    public bool DoubleOptIn { get; set; } = false;
    public string? RedirectSuccess { get; set; }
    public string? RedirectError { get; set; }
    public bool RedirectFormPost { get; set; } = true;
    public bool RedirectJsEmbed { get; set; } = false;
    public bool DisableRedirects { get; set; } = false;
    public string Language { get; set; } = "en";
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int SubmissionCount { get; set; } = 0;
    public bool ConsentRequired { get; set; } = true;
    public string? ConsentText { get; set; }
    public string? PrivacyPolicyUrl { get; set; }
    public bool CollectName { get; set; } = false;
    public string? NameLabel { get; set; }
    public string? CustomFields { get; set; } // JSON: {"key":"value", ...}
}
