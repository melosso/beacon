namespace Beacon.Core.Models;

public sealed class BucketOptions
{
    public required string Bucket { get; set; }

    /// <summary>
    /// When false, this bucket opts out of the global double opt-in setting.
    /// Defaults to true so all buckets inherit the global behaviour automatically.
    /// </summary>
    public bool DoubleOptIn { get; set; } = true;

    public DateTime? UpdatedAt { get; set; }
}
