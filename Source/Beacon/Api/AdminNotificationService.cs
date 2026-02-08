using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Beacon.Api;

public record WebhookErrorNotification(string Bucket, string ErrorMessage, int StatusCode, DateTime OccurredAt);

public interface IAdminNotificationService
{
    Task PublishAsync(WebhookErrorNotification notification);
    IAsyncEnumerable<WebhookErrorNotification> SubscribeAsync(CancellationToken cancellationToken);
}

public sealed class AdminNotificationService : IAdminNotificationService
{
    private readonly Lock _lock = new();
    private readonly List<Channel<WebhookErrorNotification>> _subscribers = [];

    public async Task PublishAsync(WebhookErrorNotification notification)
    {
        List<Channel<WebhookErrorNotification>> snapshot;
        lock (_lock)
        {
            snapshot = [.. _subscribers];
        }

        foreach (var channel in snapshot)
        {
            // Non-blocking write; drop if a subscriber's buffer is full
            channel.Writer.TryWrite(notification);
        }

        await Task.CompletedTask;
    }

    public async IAsyncEnumerable<WebhookErrorNotification> SubscribeAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateBounded<WebhookErrorNotification>(new BoundedChannelOptions(64)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });

        lock (_lock)
        {
            _subscribers.Add(channel);
        }

        try
        {
            await foreach (var notification in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return notification;
            }
        }
        finally
        {
            lock (_lock)
            {
                _subscribers.Remove(channel);
            }

            channel.Writer.TryComplete();
        }
    }
}
