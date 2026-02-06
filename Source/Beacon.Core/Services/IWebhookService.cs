using Beacon.Core.Models;

namespace Beacon.Core.Services;

public interface IWebhookService
{
    Task<WebhookConfig?> GetWebhookConfigAsync(string bucket);
    Task<string> SaveWebhookConfigAsync(string bucket, string url, string method, Dictionary<string, string>? headers, string? bodyTemplate);
    Task DeleteWebhookConfigAsync(string bucket);
    Task TriggerWebhookAsync(string bucket, WebhookTriggerData data);
}
