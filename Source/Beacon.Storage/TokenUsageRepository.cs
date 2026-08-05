using Beacon.Core.Models;
using Beacon.Core.Services;
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

    public async Task MarkTokenUsedAsync(string tokenHash, DateTime expiresAt)
    {
        var usedToken = new UsedToken
        {
            TokenHash = tokenHash,
            UsedAt = DateTime.UtcNow,
            ExpiresAt = expiresAt
        };

        _context.UsedTokens.Add(usedToken);
        await _context.SaveChangesAsync();
    }

    public async Task CleanupExpiredTokensAsync()
    {
        var cutoff = DateTime.UtcNow;
        await _context.UsedTokens
            .Where(t => t.ExpiresAt < cutoff)
            .ExecuteDeleteAsync();
    }
}
