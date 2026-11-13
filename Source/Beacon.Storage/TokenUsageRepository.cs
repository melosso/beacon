using Beacon.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Storage;

public sealed class TokenUsageRepository {
    private readonly BeaconDbContext _context;

    public TokenUsageRepository(BeaconDbContext context)
    {
        _context = context;
    }

    public async Task<bool> IsTokenUsedAsync(string tokenHash)
    {
        return await _context.UsedTokens.AnyAsync(t => t.TokenHash == tokenHash);
    }

    /// <summary>Records the token as used. Returns false if the token is already recorded.</summary>
    public async Task<bool> TryMarkUsedAsync(string tokenHash, DateTime expiresAt)
    {
        var usedToken = new UsedToken
        {
            TokenHash = tokenHash,
            UsedAt = DateTime.UtcNow,
            ExpiresAt = expiresAt
        };

        _context.UsedTokens.Add(usedToken);
        try
        {
            await _context.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateException ex) when (ConsentRepository.IsUniqueConstraintViolation(ex))
        {
            return false;
        }
        finally
        {
            // Untracked: a repeat claim in the same scope reaches the unique key.
            _context.Entry(usedToken).State = EntityState.Detached;
        }
    }

    public async Task CleanupExpiredTokensAsync()
    {
        var cutoff = DateTime.UtcNow;
        await _context.UsedTokens
            .Where(t => t.ExpiresAt < cutoff)
            .ExecuteDeleteAsync();
    }
}
