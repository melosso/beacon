using Beacon.Core.Models;

namespace Beacon.Core.Services;

public interface IApiKeyRepository
{
    Task<ApiKey?> FindByIdAsync(Guid id);
    Task<ApiKey?> FindByKeyHashAsync(string hash);
    Task<IList<ApiKey>> GetAllAsync();
    Task<ApiKey> CreateAsync(string name, string keyHash, string[] permissions,
                             bool isEnabled, DateTime? activeFrom, DateTime? activeUntil);
    Task UpdateKeyHashAsync(Guid id, string newKeyHash);
    Task UpdatePermissionsAsync(Guid id, string[] permissions);
    Task UpdateEnabledAsync(Guid id, bool isEnabled);
    Task UpdateDatesAsync(Guid id, DateTime? activeFrom, DateTime? activeUntil);
    Task UpdateLastUsedAsync(Guid id);
    Task DeleteAsync(Guid id);
}
