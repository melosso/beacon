namespace Beacon.Tokens;

public sealed class TokenOptions
{
    public required string SigningKey { get; set; }
    public int ExpiryDays { get; set; } = 60;
}
