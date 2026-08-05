using System.Text.Json;
using Beacon.Core.Models;
using Beacon.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Storage;

public sealed class ApiKeyRepository {
    private readonly BeaconDbContext _context;

    public ApiKeyRepository(BeaconDbContext context)
    {
        _context = context;
    }

    public async Task<ApiKey?> FindByIdAsync(Guid id)
    {
        return await _context.ApiKeys.FindAsync(id);
    }

    public async Task<ApiKey?> FindByKeyHashAsync(string hash)
    {
        return await _context.ApiKeys
            .FirstOrDefaultAsync(k => k.KeyHash == hash);
    }

    public async Task<IList<ApiKey>> GetAllAsync()
    {
        return await _context.ApiKeys
            .OrderByDescending(k => k.CreatedAt)
            .ToListAsync();
    }

    public async Task<ApiKey> CreateAsync(string name, string keyHash, string[] permissions,
                                          bool isEnabled, DateTime? activeFrom, DateTime? activeUntil)
    {
        var key = new ApiKey
        {
            Id = Guid.NewGuid(),
            Name = name,
            KeyHash = keyHash,
            Permissions = JsonSerializer.Serialize(permissions),
            IsEnabled = isEnabled,
            ActiveFrom = activeFrom,
            ActiveUntil = activeUntil,
            CreatedAt = DateTime.UtcNow
        };
        await _context.ApiKeys.AddAsync(key);
        await _context.SaveChangesAsync();
        return key;
    }

    public async Task UpdateKeyHashAsync(Guid id, string newKeyHash)
    {
        var key = await _context.ApiKeys.FindAsync(id);
        if (key != null)
        {
            key.KeyHash = newKeyHash;
            key.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }

    public async Task UpdatePermissionsAsync(Guid id, string[] permissions)
    {
        var key = await _context.ApiKeys.FindAsync(id);
        if (key != null)
        {
            key.Permissions = JsonSerializer.Serialize(permissions);
            key.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }

    public async Task UpdateEnabledAsync(Guid id, bool isEnabled)
    {
        var key = await _context.ApiKeys.FindAsync(id);
        if (key != null)
        {
            key.IsEnabled = isEnabled;
            key.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }

    public async Task UpdateDatesAsync(Guid id, DateTime? activeFrom, DateTime? activeUntil)
    {
        var key = await _context.ApiKeys.FindAsync(id);
        if (key != null)
        {
            key.ActiveFrom = activeFrom;
            key.ActiveUntil = activeUntil;
            key.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }

    public async Task UpdateLastUsedAsync(Guid id)
    {
        var key = await _context.ApiKeys.FindAsync(id);
        if (key != null)
        {
            key.LastUsedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }

    public async Task DeleteAsync(Guid id)
    {
        var key = await _context.ApiKeys.FindAsync(id);
        if (key != null)
        {
            _context.ApiKeys.Remove(key);
            await _context.SaveChangesAsync();
        }
    }
}
