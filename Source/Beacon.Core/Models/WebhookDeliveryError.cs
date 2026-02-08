namespace Beacon.Core.Models;

public sealed class WebhookDeliveryError
{
    public Guid Id { get; set; }
    public required string Bucket { get; set; }
    public required string ErrorMessage { get; set; }
    public int StatusCode { get; set; }
    public DateTime OccurredAt { get; set; }
    public string? RequestUrl { get; set; }
    public string? RequestMethod { get; set; }
    public int AttemptCount { get; set; }
    public string? StackTrace { get; set; }
}
