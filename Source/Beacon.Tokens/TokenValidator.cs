using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Beacon.Tokens;

public sealed class TokenValidator
{
    private readonly byte[] _signingKey;

    public TokenValidator(TokenOptions options)
    {
        _signingKey = Convert.FromBase64String(options.SigningKey);
    }

    public TokenValidationResult Validate(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return TokenValidationResult.Invalid("Token is empty");
        }

        var parts = token.Split('.');
        if (parts.Length != 3)
        {
            return TokenValidationResult.Invalid("Token format invalid");
        }

        var version = parts[0];
        var payloadBase64 = parts[1];
        var signature = parts[2];

        if (version != "v1")
        {
            return TokenValidationResult.Invalid("Unsupported token version");
        }

        var expectedSignature = ComputeSignature(payloadBase64);
        if (!CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(signature),
            Encoding.UTF8.GetBytes(expectedSignature)))
        {
            return TokenValidationResult.Invalid("Signature invalid");
        }

        TokenPayload? payload;
        try
        {
            var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(payloadBase64));
            payload = JsonSerializer.Deserialize<TokenPayload>(payloadJson);

            if (payload is null)
            {
                return TokenValidationResult.Invalid("Payload deserialization failed");
            }
        }
        catch (Exception)
        {
            return TokenValidationResult.Invalid("Payload decode failed");
        }

        if (payload.IsExpired)
        {
            return TokenValidationResult.Expired(payload);
        }

        return TokenValidationResult.Valid(payload);
    }

    private string ComputeSignature(string payload)
    {
        using var hmac = new HMACSHA256(_signingKey);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Base64UrlEncode(hash);
    }

    private static string Base64UrlEncode(byte[] data)
    {
        return Convert.ToBase64String(data)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static byte[] Base64UrlDecode(string data)
    {
        var padded = data
            .Replace('-', '+')
            .Replace('_', '/');

        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }

        return Convert.FromBase64String(padded);
    }
}

public sealed class TokenValidationResult
{
    public bool IsValid { get; private init; }
    public bool IsExpired { get; private init; }
    public string? Error { get; private init; }
    public TokenPayload? Payload { get; private init; }

    private TokenValidationResult() { }

    public static TokenValidationResult Valid(TokenPayload payload) => new()
    {
        IsValid = true,
        IsExpired = false,
        Payload = payload
    };

    public static TokenValidationResult Expired(TokenPayload payload) => new()
    {
        IsValid = false,
        IsExpired = true,
        Error = "Token expired",
        Payload = payload
    };

    public static TokenValidationResult Invalid(string error) => new()
    {
        IsValid = false,
        IsExpired = false,
        Error = error
    };
}
