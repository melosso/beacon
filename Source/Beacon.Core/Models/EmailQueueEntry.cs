namespace Beacon.Core.Models;

public sealed class EmailQueueEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Bucket { get; set; }
    public required string EncryptedEmail { get; set; }
    public required string EmailHash { get; set; }
    public required string Permission { get; set; }
    public string Language { get; set; } = "en";
    public required string ConfirmationToken { get; set; }
    public required string ConfirmationUrl { get; set; }
    public EmailQueueStatus Status { get; set; } = EmailQueueStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SentAt { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public int AttemptCount { get; set; } = 0;
    public string? LastError { get; set; }
    public DateTime? NextAttemptAt { get; set; }
}

public enum EmailQueueStatus
{
    Pending = 0,
    Sent = 1,
    Confirmed = 2,
    Failed = 3,
    Expired = 4,
    Cancelled = 5
}
