using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Beacon.Api;

public record WebhookErrorNotification(string Bucket, string ErrorMessage, int StatusCode, DateTime OccurredAt);
public record ConsentUpdateNotification(string Bucket);

public sealed class AdminNotificationService {
    // Used as a set: ConcurrentDictionary gives lock-free add, remove and iteration.
    private readonly ConcurrentDictionary<Channel<object>, byte> _subscribers = new();

    internal int SubscriberCount => _subscribers.Count;

    public Task PublishAsync(WebhookErrorNotification notification) => Publish(notification);

    public Task PublishConsentUpdateAsync(ConsentUpdateNotification notification) => Publish(notification);

    private Task Publish(object notification)
    {
        foreach (var channel in _subscribers.Keys)
            channel.Writer.TryWrite(notification);

        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<object> SubscribeAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateBounded<object>(new BoundedChannelOptions(64)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });

        _subscribers.TryAdd(channel, 0);

        try
        {
            await foreach (var notification in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return notification;
            }
        }
        finally
        {
            _subscribers.TryRemove(channel, out _);
            channel.Writer.TryComplete();
        }
    }
}
