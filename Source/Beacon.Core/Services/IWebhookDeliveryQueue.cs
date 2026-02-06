namespace Beacon.Core.Services;

public interface IWebhookDeliveryQueue
{
    ValueTask EnqueueAsync(WebhookDeliveryMessage message, CancellationToken cancellationToken = default);
    IAsyncEnumerable<WebhookDeliveryMessage> DequeueAllAsync(CancellationToken cancellationToken);
}
