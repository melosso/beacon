using Beacon.Core.Models;

namespace Beacon.Core.Services;

public interface IWebhookRepository
{
    Task<WebhookConfig?> GetByBucketAsync(string bucket);
    Task<List<WebhookConfig>> GetAllAsync();
    Task UpsertAsync(WebhookConfig config);
    Task DeleteByBucketAsync(string bucket);
    Task UpdateTriggerStatsAsync(Guid id, DateTime triggeredAt);
}
