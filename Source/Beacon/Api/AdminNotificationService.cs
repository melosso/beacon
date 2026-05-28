using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Beacon.Api;

public record WebhookErrorNotification(string Bucket, string ErrorMessage, int StatusCode, DateTime OccurredAt);
public record ConsentUpdateNotification(string Bucket);

public interface IAdminNotificationService
{
    Task PublishAsync(WebhookErrorNotification notification);
    Task PublishConsentUpdateAsync(ConsentUpdateNotification notification);
    IAsyncEnumerable<WebhookErrorNotification> SubscribeAsync(CancellationToken cancellationToken);
    IAsyncEnumerable<object> SubscribeAllAsync(CancellationToken cancellationToken);
}

public sealed class AdminNotificationService : IAdminNotificationService
{
    private readonly Lock _lock = new();
    private readonly List<Channel<WebhookErrorNotification>> _webhookSubscribers = [];
    private readonly List<Channel<object>> _allSubscribers = [];

    internal int WebhookSubscriberCount { get { lock (_lock) return _webhookSubscribers.Count; } }

    public Task PublishAsync(WebhookErrorNotification notification)
    {
        List<Channel<WebhookErrorNotification>> webhookSnapshot;
        List<Channel<object>> allSnapshot;
        lock (_lock)
        {
            webhookSnapshot = [.. _webhookSubscribers];
            allSnapshot = [.. _allSubscribers];
        }

        foreach (var channel in webhookSnapshot)
            channel.Writer.TryWrite(notification);

        foreach (var channel in allSnapshot)
            channel.Writer.TryWrite(notification);

        return Task.CompletedTask;
    }

    public Task PublishConsentUpdateAsync(ConsentUpdateNotification notification)
    {
        List<Channel<object>> snapshot;
        lock (_lock)
        {
            snapshot = [.. _allSubscribers];
        }

        foreach (var channel in snapshot)
            channel.Writer.TryWrite(notification);

        return Task.CompletedTask;
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
            _webhookSubscribers.Add(channel);
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
                _webhookSubscribers.Remove(channel);
            }

            channel.Writer.TryComplete();
        }
    }

    public async IAsyncEnumerable<object> SubscribeAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateBounded<object>(new BoundedChannelOptions(64)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });

        lock (_lock)
        {
            _allSubscribers.Add(channel);
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
                _allSubscribers.Remove(channel);
            }

            channel.Writer.TryComplete();
        }
    }
}
