using System.Text.Json.Serialization;

namespace Beacon.Core.Models;

public sealed record BrandIdentitySettings
{
    [JsonPropertyName("primaryAccent")]  public string? PrimaryAccent { get; init; }
    [JsonPropertyName("surfaceColour")]  public string? SurfaceColour { get; init; }
    [JsonPropertyName("theme")]          public string Theme { get; init; } = "system";
    [JsonPropertyName("logo")]           public AssetObject? Logo { get; init; }
    [JsonPropertyName("pageTitle")]      public string? PageTitle { get; init; }
    [JsonPropertyName("pageBody")]       public string? PageBody { get; init; }
    [JsonPropertyName("emailTitle")]     public string? EmailTitle { get; init; }
    [JsonPropertyName("emailBody")]      public string? EmailBody { get; init; }
    [JsonPropertyName("confirmTitle")]   public string? ConfirmTitle { get; init; }
    [JsonPropertyName("confirmMsg")]     public string? ConfirmMsg { get; init; }
    [JsonPropertyName("footer")]         public string? Footer { get; init; }
    [JsonPropertyName("browserTitle")]   public string? BrowserTitle { get; init; }
    [JsonPropertyName("font")]           public string? Font { get; init; }
}

public sealed record AssetObject
{
    [JsonPropertyName("type")]  public required string Type { get; init; }
    [JsonPropertyName("data")]  public string? Data { get; init; }
    [JsonPropertyName("url")]   public string? Url { get; init; }
}
