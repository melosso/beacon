namespace Beacon.Core.Models;

public sealed class BucketIdentity
{
    public required string Bucket { get; set; }

    public int BrandIdentityId { get; set; }

    public BrandIdentity BrandIdentity { get; set; } = null!;
}
