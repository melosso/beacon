using System.Buffers.Text;
using Beacon.Core.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Beacon.Tokens;

public sealed class TokenValidator
{
    private readonly byte[] _signingKey;
    private readonly byte[]? _payloadKey;

    public TokenValidator(TokenOptions options)
    {
        _signingKey = Convert.FromBase64String(options.SigningKey);
        _payloadKey = options.PayloadEncryptionKey is { Length: > 0 } key
            ? Convert.FromBase64String(key)
            : null;
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

        // v1 payloads are plaintext, v2 are sealed. v1 stays accepted so links already emailed keep working.
        if (version is not ("v1" or "v2"))
        {
            return TokenValidationResult.Invalid("Unsupported token version");
        }

        if (version == "v2" && _payloadKey is null)
        {
            return TokenValidationResult.Invalid("Sealed token received but no payload key is configured");
        }

        var expectedSignature = ComputeSignature(
            version == "v1" ? payloadBase64 : $"{version}.{payloadBase64}");
        if (!CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(signature),
            Encoding.UTF8.GetBytes(expectedSignature)))
        {
            return TokenValidationResult.Invalid("Signature invalid");
        }

        TokenPayload? payload;
        try
        {
            var payloadBytes = Base64Url.DecodeFromChars(payloadBase64);

            if (version == "v2")
            {
                payloadBytes = AesGcmCipher.Open(payloadBytes, _payloadKey!)
                    ?? throw new CryptographicException("Sealed payload could not be opened");
            }

            payload = JsonSerializer.Deserialize<TokenPayload>(payloadBytes);

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
        return Base64Url.EncodeToString(hash);
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
