namespace Beacon.Core.Models;

public sealed class UsedToken
{
    public required string TokenHash { get; set; }
    public DateTime UsedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}
