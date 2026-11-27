namespace Beacon.Core.Models;

public sealed class OidcPendingFlow
{
    public required string State { get; set; }
    public int ProviderId { get; set; }
    public required string Verifier { get; set; }
    public required string Nonce { get; set; }
    public required string RedirectUri { get; set; }
    public DateTime ExpiresAt { get; set; }
    public Guid? LinkUserId { get; set; }
}
