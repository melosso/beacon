namespace Beacon.Core.Models;

public sealed class ApiKey
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string KeyHash { get; set; }
    public required string Permissions { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTime? ActiveFrom { get; set; }
    public DateTime? ActiveUntil { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
}
