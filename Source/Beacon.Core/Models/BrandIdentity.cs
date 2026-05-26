namespace Beacon.Core.Models;

public sealed class BrandIdentity
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public string Settings { get; set; } = "{}";

    public bool IsDefault { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<BucketIdentity> BucketMappings { get; set; } = [];
}
