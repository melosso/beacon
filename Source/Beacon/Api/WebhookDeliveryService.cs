using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Beacon.Core.Models;
using Beacon.Core.Services;

namespace Beacon.Api;

public sealed class WebhookDeliveryService : BackgroundService
{
    private const int MaxRetries = 3;
    private static readonly TimeSpan[] RetryDelays = [
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30)
    ];

    private readonly IWebhookDeliveryQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAdminNotificationService _notifications;
    private readonly ILogger<WebhookDeliveryService> _logger;

    public WebhookDeliveryService(
        IWebhookDeliveryQueue queue,
        IServiceScopeFactory scopeFactory,
        IAdminNotificationService notifications,
        ILogger<WebhookDeliveryService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _notifications = notifications;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in _queue.DequeueAllAsync(stoppingToken))
        {
            await DeliverWithRetryAsync(message, stoppingToken);
        }
    }

    private async Task DeliverWithRetryAsync(WebhookDeliveryMessage message, CancellationToken stoppingToken)
    {
        Exception? lastException = null;

        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            try
            {
                if (attempt > 0)
                {
                    var delay = RetryDelays[Math.Min(attempt - 1, RetryDelays.Length - 1)];
                    _logger.LogInformation(
                        "Retrying webhook delivery for bucket {Bucket} (attempt {Attempt}/{MaxRetries})",
                        message.Bucket, attempt, MaxRetries);
                    await Task.Delay(delay, stoppingToken);
                }

                var resolvedAddress = await ResolveAndValidateAsync(message.Url);
                if (resolvedAddress is null)
                {
                    _logger.LogWarning(
                        "Webhook delivery blocked for bucket {Bucket}: URL {Url} resolves to a private/reserved address",
                        message.Bucket, message.Url);
                    var ssrfError = "URL resolves to a private/reserved address (SSRF blocked)";
                    await PersistErrorAsync(message.Bucket, ssrfError, 0,
                        message.Url, message.Method, attempt + 1, null);
                    await PublishErrorNotificationAsync(message.Bucket, ssrfError, 0);
                    return; // Don't retry SSRF blocks
                }

                await SendAsync(message, resolvedAddress);

                // Update trigger stats using a fresh scope
                using var scope = _scopeFactory.CreateScope();
                var repository = scope.ServiceProvider.GetRequiredService<IWebhookRepository>();
                await repository.UpdateTriggerStatsAsync(message.WebhookConfigId, DateTime.UtcNow);

                _logger.LogDebug("Webhook delivered for bucket {Bucket}", message.Bucket);
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                lastException = ex;
                var summary = SummarizeNetworkException(ex);
                if (summary is not null)
                    _logger.LogWarning(
                        "Webhook delivery failed for bucket {Bucket} (attempt {Attempt}/{MaxRetries}): {Reason}",
                        message.Bucket, attempt + 1, MaxRetries + 1, summary);
                else
                    _logger.LogWarning(ex,
                        "Webhook delivery failed for bucket {Bucket} (attempt {Attempt}/{MaxRetries})",
                        message.Bucket, attempt + 1, MaxRetries + 1);

                // Notify on first failure so the user knows something went wrong immediately.
                // The final failure notification is sent after the loop with the persisted error.
                if (attempt == 0)
                {
                    var firstErrorMessage = summary ?? lastException.Message;
                    await PublishErrorNotificationAsync(message.Bucket, $"{firstErrorMessage} (retrying…)", 0);
                }
            }
        }

        _logger.LogError(
            "Webhook delivery permanently failed for bucket {Bucket} after {MaxRetries} retries",
            message.Bucket, MaxRetries);

        // Persist the delivery error
        var statusCode = 0;
        var errorMessage = lastException?.Message ?? "Unknown error";
        if (lastException is HttpRequestException httpEx && httpEx.StatusCode.HasValue)
        {
            statusCode = (int)httpEx.StatusCode.Value;
            errorMessage = $"HTTP {statusCode}: {httpEx.Message}";
        }

        await PersistErrorAsync(message.Bucket, errorMessage, statusCode,
            message.Url, message.Method, MaxRetries + 1, lastException?.ToString());
        await PublishErrorNotificationAsync(message.Bucket, errorMessage, statusCode);
    }

    private async Task PersistErrorAsync(string bucket, string errorMessage, int statusCode,
        string? requestUrl, string? requestMethod, int attemptCount, string? stackTrace)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IWebhookRepository>();

            await repository.AddErrorAsync(new WebhookDeliveryError
            {
                Id = Guid.NewGuid(),
                Bucket = bucket,
                ErrorMessage = errorMessage,
                StatusCode = statusCode,
                OccurredAt = DateTime.UtcNow,
                RequestUrl = requestUrl,
                RequestMethod = requestMethod,
                AttemptCount = attemptCount,
                StackTrace = stackTrace
            });

            await repository.PruneErrorsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist webhook delivery error for bucket {Bucket}", bucket);
        }
    }

    private async Task PublishErrorNotificationAsync(string bucket, string errorMessage, int statusCode)
    {
        try
        {
            await _notifications.PublishAsync(new WebhookErrorNotification(bucket, errorMessage, statusCode, DateTime.UtcNow));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish webhook error notification for bucket {Bucket}", bucket);
        }
    }

    private static async Task SendAsync(WebhookDeliveryMessage message, IPAddress resolvedAddress)
    {
        Uri.TryCreate(message.Url, UriKind.Absolute, out var uri);
        var endpoint = new IPEndPoint(resolvedAddress, uri!.Port);

        var handler = new SocketsHttpHandler
        {
            ConnectCallback = async (context, cancellationToken) =>
            {
                var socket = new Socket(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    await socket.ConnectAsync(endpoint, cancellationToken);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            }
        };

        using var httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };

        var request = new HttpRequestMessage(new HttpMethod(message.Method), message.Url);

        if (message.Headers != null)
        {
            foreach (var header in message.Headers)
            {
                if (header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                    continue;

                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        // Add HMAC signature header
        if (!string.IsNullOrEmpty(message.SignatureHeader))
        {
            request.Headers.TryAddWithoutValidation("X-Beacon-Signature", message.SignatureHeader);
        }

        if (!string.IsNullOrEmpty(message.Body))
        {
            var contentType = message.Headers?.FirstOrDefault(h =>
                h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)).Value
                ?? "application/json";

            request.Content = new StringContent(message.Body, Encoding.UTF8, contentType);
        }

        var response = await httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<IPAddress?> ResolveAndValidateAsync(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return null;

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(uri.Host);
            if (addresses.Length == 0 || addresses.Any(IsPrivateOrReserved))
                return null;

            return addresses[0];
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Returns a short, human-readable summary for well-known transient network errors
    /// (SSL failures, socket errors, timeouts). Returns null for unexpected exceptions,
    /// which should be logged with the full stack trace.
    /// </summary>
    private static string? SummarizeNetworkException(Exception ex)
    {
        // Walk the inner exception chain to find the root cause
        var inner = ex;
        while (inner.InnerException is not null)
            inner = inner.InnerException;

        return ex switch
        {
            TaskCanceledException or OperationCanceledException
                => "Request timed out",

            HttpRequestException { InnerException: System.Security.Authentication.AuthenticationException }
                => $"SSL error: {inner.Message}",

            HttpRequestException { InnerException: SocketException se }
                => $"Connection error: {se.Message}",

            HttpRequestException httpEx when httpEx.StatusCode.HasValue
                => null, // unexpected HTTP status

            HttpRequestException
                => $"Network error: {inner.Message}",

            _ => null
        };
    }

    private static bool IsPrivateOrReserved(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
            return true;

        // Map IPv4-mapped IPv6 to IPv4 for consistent checking
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] switch
            {
                10 => true,                                          // 10.0.0.0/8
                127 => true,                                         // 127.0.0.0/8
                169 when bytes[1] == 254 => true,                    // 169.254.0.0/16 (link-local)
                172 when bytes[1] >= 16 && bytes[1] <= 31 => true,   // 172.16.0.0/12
                192 when bytes[1] == 168 => true,                    // 192.168.0.0/16
                0 => true,                                           // 0.0.0.0/8
                100 when bytes[1] >= 64 && bytes[1] <= 127 => true,  // 100.64.0.0/10 (CGN)
                _ => false
            };
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal;
        }

        return false;
    }
}
