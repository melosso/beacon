using Beacon.Core.Models;

namespace Beacon.Core.Services;

public interface IUserRepository
{
    Task<User?> FindByUsernameAsync(string username);
    Task<User?> FindByApiKeyHashAsync(string hash);
    Task<IList<User>> GetAllAsync();
    Task<User> CreateAsync(string username, string passwordHash, string salt, string role, string apiKeyHash);
    Task UpdatePasswordAsync(Guid id, string newHash, string newSalt);
    Task UpdateApiKeyAsync(Guid id, string newApiKeyHash);
    Task UpdateRoleAsync(Guid id, string role);
    Task SetLastLoginAsync(Guid id);
    Task DeleteAsync(Guid id);
    Task<int> CountAsync();
}
