using Beacon.Core.Models;
using Beacon.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Storage;

public sealed class WebhookRepository : IWebhookRepository
{
    private readonly BeaconDbContext _context;

    public WebhookRepository(BeaconDbContext context)
    {
        _context = context;
    }

    public async Task<WebhookConfig?> GetByBucketAsync(string bucket)
    {
        return await _context.WebhookConfigs
            .FirstOrDefaultAsync(w => w.Bucket == bucket);
    }

    public async Task<List<WebhookConfig>> GetAllAsync()
    {
        return await _context.WebhookConfigs.ToListAsync();
    }

    public async Task UpsertAsync(WebhookConfig config)
    {
        var existing = await GetByBucketAsync(config.Bucket);
        if (existing != null)
        {
            existing.EncryptedUrl = config.EncryptedUrl;
            existing.EncryptedMethod = config.EncryptedMethod;
            existing.EncryptedHeaders = config.EncryptedHeaders;
            existing.BodyTemplate = config.BodyTemplate;
            existing.IsEnabled = config.IsEnabled;
            _context.WebhookConfigs.Update(existing);
        }
        else
        {
            config.CreatedAt = DateTime.UtcNow;
            await _context.WebhookConfigs.AddAsync(config);
        }
        
        await _context.SaveChangesAsync();
    }

    public async Task DeleteByBucketAsync(string bucket)
    {
        var config = await GetByBucketAsync(bucket);
        if (config != null)
        {
            _context.WebhookConfigs.Remove(config);
            await _context.SaveChangesAsync();
        }
    }

    public async Task UpdateTriggerStatsAsync(Guid id, DateTime triggeredAt)
    {
        var config = await _context.WebhookConfigs.FindAsync(id);
        if (config != null)
        {
            config.LastTriggeredAt = triggeredAt;
            config.TriggerCount++;
            await _context.SaveChangesAsync();
        }
    }
}
