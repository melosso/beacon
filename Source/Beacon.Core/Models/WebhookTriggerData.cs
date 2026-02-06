namespace Beacon.Core.Models;

public sealed class WebhookTriggerData
{
    public required string Bucket { get; set; }
    public required string Email { get; set; }
    public required string EmailHash { get; set; }
    public required List<PermissionState> Permissions { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string? CustomFields { get; set; }
}

public sealed class PermissionState
{
    public required string Permission { get; set; }
    public required ConsentStatus Status { get; set; }
}
