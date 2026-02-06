using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WebhookDeliveryService> _logger;

    public WebhookDeliveryService(
        IWebhookDeliveryQueue queue,
        IHttpClientFactory httpClientFactory,
        IServiceScopeFactory scopeFactory,
        ILogger<WebhookDeliveryService> logger)
    {
        _queue = queue;
        _httpClientFactory = httpClientFactory;
        _scopeFactory = scopeFactory;
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

                // SSRF check at send time: resolve and validate the target IP
                if (!await IsUrlSafeAsync(message.Url))
                {
                    _logger.LogWarning(
                        "Webhook delivery blocked for bucket {Bucket}: URL {Url} resolves to a private/reserved address",
                        message.Bucket, message.Url);
                    return; // Don't retry SSRF blocks
                }

                await SendAsync(message);

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
                _logger.LogWarning(ex,
                    "Webhook delivery failed for bucket {Bucket} (attempt {Attempt}/{MaxRetries})",
                    message.Bucket, attempt + 1, MaxRetries + 1);
            }
        }

        _logger.LogError(
            "Webhook delivery permanently failed for bucket {Bucket} after {MaxRetries} retries",
            message.Bucket, MaxRetries);
    }

    private async Task SendAsync(WebhookDeliveryMessage message)
    {
        using var httpClient = _httpClientFactory.CreateClient("WebhookClient");
        httpClient.Timeout = TimeSpan.FromSeconds(10);

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

    private static async Task<bool> IsUrlSafeAsync(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(uri.Host);
            return addresses.All(addr => !IsPrivateOrReserved(addr));
        }
        catch
        {
            return false;
        }
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
