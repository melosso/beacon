namespace Beacon.Core.Models;

public sealed class WebhookDeliveryError
{
    public Guid Id { get; set; }
    public required string Bucket { get; set; }
    public required string ErrorMessage { get; set; }
    public int StatusCode { get; set; }
    public DateTime OccurredAt { get; set; }
}
