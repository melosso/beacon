namespace Beacon.Core.Models;

public class BucketPermission
{
    public string Bucket { get; set; } = string.Empty;
    public string Permission { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
