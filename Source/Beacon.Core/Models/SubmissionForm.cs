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
    public string Language { get; set; } = "en";
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int SubmissionCount { get; set; } = 0;
}
