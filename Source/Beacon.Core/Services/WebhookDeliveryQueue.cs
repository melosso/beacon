using System.Threading.Channels;

namespace Beacon.Core.Services;

public sealed class WebhookDeliveryQueue : IWebhookDeliveryQueue
{
    private readonly Channel<WebhookDeliveryMessage> _channel =
        Channel.CreateBounded<WebhookDeliveryMessage>(new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });

    public ValueTask EnqueueAsync(WebhookDeliveryMessage message, CancellationToken cancellationToken = default)
    {
        return _channel.Writer.WriteAsync(message, cancellationToken);
    }

    public IAsyncEnumerable<WebhookDeliveryMessage> DequeueAllAsync(CancellationToken cancellationToken)
    {
        return _channel.Reader.ReadAllAsync(cancellationToken);
    }
}
