namespace Beacon.Core.Services;

public sealed class WebhookDeliveryMessage
{
    public required Guid WebhookConfigId { get; init; }
    public required string Url { get; init; }
    public required string Method { get; init; }
    public Dictionary<string, string>? Headers { get; init; }
    public string? Body { get; init; }
    public string? SignatureHeader { get; init; }
    public required string Bucket { get; init; }
}
