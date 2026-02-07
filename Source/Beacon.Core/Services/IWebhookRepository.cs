using Beacon.Core.Models;

namespace Beacon.Core.Services;

public interface IWebhookRepository
{
    Task<WebhookConfig?> GetByBucketAsync(string bucket);
    Task<List<WebhookConfig>> GetAllAsync();
    Task UpsertAsync(WebhookConfig config);
    Task DeleteByBucketAsync(string bucket);
    Task UpdateTriggerStatsAsync(Guid id, DateTime triggeredAt);
    Task AddErrorAsync(WebhookDeliveryError error);
    Task<List<WebhookDeliveryError>> GetRecentErrorsAsync(string bucket, int count = 5);
    Task PruneErrorsAsync(int retentionDays = 14);
}
