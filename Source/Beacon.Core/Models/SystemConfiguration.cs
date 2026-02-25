namespace Beacon.Core.Models;

public sealed class SystemConfiguration
{
    public int Id { get; set; } = 1;
    public string Configuration { get; set; } = "{}";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
