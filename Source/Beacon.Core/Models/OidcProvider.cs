namespace Beacon.Core.Models;

public sealed class OidcProvider
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Slug { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public string Authority { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;

    public string EncryptedClientSecret { get; set; } = string.Empty;

    public string Scopes { get; set; } = "openid profile email";
    public string UsernameClaim { get; set; } = "preferred_username";
    public string EmailClaim { get; set; } = "email";
    public bool IsEnabled { get; set; }

    public bool CreateAccounts { get; set; }

    public string CreatedRole { get; set; } = "user";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
