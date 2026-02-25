using System.Text.Json;
using Beacon.Core.Models;
using Beacon.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Beacon.Storage;

public sealed class SystemConfigurationService : ISystemConfigurationService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private volatile SystemConfig _cache;

    public SystemConfigurationService(IServiceScopeFactory scopeFactory, SystemConfig initial)
    {
        _scopeFactory = scopeFactory;
        _cache = initial;
    }

    public SystemConfig Get() => _cache;

    public async Task SaveAsync(SystemConfig config)
    {
        await _writeLock.WaitAsync();
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BeaconDbContext>();

            var json = JsonSerializer.Serialize(config);
            var entity = await db.SystemConfigurations.FindAsync(1);

            if (entity is null)
            {
                db.SystemConfigurations.Add(new SystemConfiguration
                {
                    Id = 1,
                    Configuration = json,
                    UpdatedAt = DateTime.UtcNow
                });
            }
            else
            {
                entity.Configuration = json;
                entity.UpdatedAt = DateTime.UtcNow;
            }

            await db.SaveChangesAsync();
            _cache = config;
        }
        finally
        {
            _writeLock.Release();
        }
    }
}
