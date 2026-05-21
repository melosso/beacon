namespace Beacon.Core.Models;

public sealed class ConsentAuditEntry
{
    public Guid Id { get; set; }
    public required string Bucket { get; set; }
    public required string EmailHash { get; set; }
    public required string Permission { get; set; }
    public ConsentStatus? OldStatus { get; set; }
    public ConsentStatus NewStatus { get; set; }
    public ConsentSource Source { get; set; }
    public string? ActorId { get; set; }
    public DateTime ChangedAt { get; set; }
    public string? IpAddress { get; set; }
    public string? CustomFields { get; set; }
}
