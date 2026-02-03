namespace Beacon.Core.Services;

public interface ITokenUsageRepository
{
    Task<bool> IsTokenUsedAsync(string tokenHash);
    Task MarkTokenUsedAsync(string tokenHash, DateTime expiresAt);
    Task CleanupExpiredTokensAsync();
}
