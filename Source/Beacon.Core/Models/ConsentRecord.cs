namespace Beacon.Core.Models;

public sealed class ConsentRecord
{
    public Guid Id { get; set; }
    public required string Bucket { get; set; }
    public required string EmailHash { get; set; }
    public string? EncryptedEmail { get; set; }
    public required string Permission { get; set; }
    public ConsentStatus Status { get; set; }
    public ConsentSource Source { get; set; }
    public DateTime ChangedAt { get; set; }
    public string? TokenHash { get; set; }
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// Optional custom fields stored as JSON. Set during token generation,
    /// returned when fetching bucket records.
    /// </summary>
    public string? CustomFields { get; set; }
}
