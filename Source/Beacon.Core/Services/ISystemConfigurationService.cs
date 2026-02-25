using System.Text.Json.Serialization;

namespace Beacon.Core.Services;

public sealed class SystemConfig
{
    [JsonPropertyName("allowDbLookup")]
    public bool AllowDbLookup { get; set; } = false;

    [JsonPropertyName("defaultLanguage")]
    public string DefaultLanguage { get; set; } = "en";
}

public interface ISystemConfigurationService
{
    SystemConfig Get();
    Task SaveAsync(SystemConfig config);
}
