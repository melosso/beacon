using Beacon.Core.Models;
using Beacon.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Storage;

public sealed class UserRepository : IUserRepository
{
    private readonly BeaconDbContext _context;

    public UserRepository(BeaconDbContext context)
    {
        _context = context;
    }

    public async Task<User?> FindByUsernameAsync(string username)
    {
        var lower = username.ToLowerInvariant();
        return await _context.Users
            .FirstOrDefaultAsync(u => u.Username.ToLower() == lower);
    }

    public async Task<User?> FindByApiKeyHashAsync(string hash)
    {
        return await _context.Users
            .FirstOrDefaultAsync(u => u.ApiKeyHash == hash);
    }

    public async Task<IList<User>> GetAllAsync()
    {
        return await _context.Users
            .OrderByDescending(u => u.CreatedAt)
            .ToListAsync();
    }

    public async Task<User> CreateAsync(string username, string passwordHash, string salt, string role, string apiKeyHash)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = username,
            PasswordHash = passwordHash,
            Salt = salt,
            Role = role,
            ApiKeyHash = apiKeyHash,
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow
        };
        await _context.Users.AddAsync(user);
        await _context.SaveChangesAsync();
        return user;
    }

    public async Task UpdatePasswordAsync(Guid id, string newHash, string newSalt)
    {
        var user = await _context.Users.FindAsync(id);
        if (user != null)
        {
            user.PasswordHash = newHash;
            user.Salt = newSalt;
            user.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }

    public async Task UpdateApiKeyAsync(Guid id, string newApiKeyHash)
    {
        var user = await _context.Users.FindAsync(id);
        if (user != null)
        {
            user.ApiKeyHash = newApiKeyHash;
            user.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }

    public async Task UpdateRoleAsync(Guid id, string role)
    {
        var user = await _context.Users.FindAsync(id);
        if (user != null)
        {
            user.Role = role;
            user.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }

    public async Task SetLastLoginAsync(Guid id)
    {
        var user = await _context.Users.FindAsync(id);
        if (user != null)
        {
            user.LastLoginAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }

    public async Task DeleteAsync(Guid id)
    {
        var user = await _context.Users.FindAsync(id);
        if (user != null)
        {
            _context.Users.Remove(user);
            await _context.SaveChangesAsync();
        }
    }

    public async Task<int> CountAsync()
    {
        return await _context.Users.CountAsync();
    }
}
