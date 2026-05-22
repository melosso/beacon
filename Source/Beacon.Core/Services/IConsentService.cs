using Beacon.Core.Models;

namespace Beacon.Core.Services;

public interface IConsentService
{
    Task<ConsentStatus> CheckAsync(string bucket, string email, string permission);
    Task ProcessOptOutAsync(string bucket, string email, string[] permissions, string token, ConsentSource source, string? customFieldsJson = null);
    Task OverrideAsync(string bucket, string email, string permission, ConsentStatus status,
        string? customFieldsJson = null, string? actorId = null,
        ConsentSource source = ConsentSource.Admin, string? consentText = null);

    /// <summary>
    /// Ensures a consent record exists. Creates with the specified status if not found,
    /// but does not update existing records (preserves user preferences).
    /// Returns true if a new record was created, false if it already existed.
    /// </summary>
    Task<bool> EnsureAsync(string bucket, string email, string permission, ConsentStatus status,
        string? customFieldsJson = null, string? consentText = null,
        ConsentSource source = ConsentSource.Admin, string? actorId = null);

    Task<IDisposable> BeginTransactionAsync();
    Task CommitTransactionAsync();
}
