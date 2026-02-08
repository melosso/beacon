namespace Beacon.Core.Models;

public sealed class WebhookConfig
{
    public Guid Id { get; set; }
    public required string Bucket { get; set; }
    public required string EncryptedUrl { get; set; }
    public required string EncryptedMethod { get; set; }
    public string? EncryptedHeaders { get; set; }
    public string? EncryptedSecret { get; set; }
    public string? BodyTemplate { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? LastTriggeredAt { get; set; }
    public int TriggerCount { get; set; } = 0;
}
