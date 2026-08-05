namespace Beacon.Tokens;

public sealed class TokenOptions
{
    public required string SigningKey { get; set; }
    public int ExpiryDays { get; set; } = 60;

    /// <summary>
    /// Base64 256-bit key used to seal the token payload (v2 tokens). When null, tokens are issued in
    /// the legacy v1 format with a plaintext payload.
    /// </summary>
    public string? PayloadEncryptionKey { get; set; }
}
