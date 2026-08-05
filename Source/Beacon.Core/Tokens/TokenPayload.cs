using System.Text.Json.Serialization;

namespace Beacon.Tokens;

public sealed class TokenPayload
{
    [JsonPropertyName("b")]
    public required string Bucket { get; set; }

    [JsonPropertyName("e")]
    public required string Email { get; set; }

    [JsonPropertyName("p")]
    public required string[] Permissions { get; set; }

    [JsonPropertyName("iat")]
    public long IssuedAt { get; set; }

    [JsonPropertyName("exp")]
    public long ExpiresAt { get; set; }

    [JsonPropertyName("n")]
    public required string Nonce { get; set; }

    /// <summary>
    /// Allow replay - if true, token can be reused multiple times until expiry.
    /// If false, token can only be used once (default: true).
    /// </summary>
    [JsonPropertyName("r")]
    public bool AllowReplay { get; set; } = true;

    /// <summary>
    /// Language code for the preference page (e.g., "en", "de", "fr", "nl", "pl", "es", "it", "pt", "ja").
    /// Default: "en" (English).
    /// </summary>
    [JsonPropertyName("l")]
    public string Language { get; set; } = "en";

    [JsonIgnore]
    public DateTime IssuedAtUtc => DateTimeOffset.FromUnixTimeSeconds(IssuedAt).UtcDateTime;

    [JsonIgnore]
    public DateTime ExpiresAtUtc => DateTimeOffset.FromUnixTimeSeconds(ExpiresAt).UtcDateTime;

    [JsonIgnore]
    public bool IsExpired => DateTimeOffset.UtcNow.ToUnixTimeSeconds() > ExpiresAt;
}
