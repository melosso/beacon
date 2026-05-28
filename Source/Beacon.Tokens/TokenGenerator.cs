using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Beacon.Tokens;

public sealed class TokenGenerator
{
    private readonly byte[] _signingKey;
    private readonly int _defaultExpiryDays;
    private readonly TimeProvider _timeProvider;

    public TokenGenerator(TokenOptions options, TimeProvider? timeProvider = null)
    {
        _signingKey = Convert.FromBase64String(options.SigningKey);
        _defaultExpiryDays = options.ExpiryDays;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public string Generate(string bucket, string email, string[] permissions, GenerateTokenRequest? options = null)
    {
        var normalizedEmail = NormalizeEmail(email);
        var now = _timeProvider.GetUtcNow();
        var expiryDays = options?.ExpiryDays ?? _defaultExpiryDays;
        var allowReplay = options?.AllowReplay ?? true;
        var language = options?.Language ?? "en";

        var payload = new TokenPayload
        {
            Bucket = bucket,
            Email = normalizedEmail,
            Permissions = permissions,
            IssuedAt = now.ToUnixTimeSeconds(),
            ExpiresAt = now.AddDays(expiryDays).ToUnixTimeSeconds(),
            Nonce = GenerateNonce(),
            AllowReplay = allowReplay,
            Language = language
        };

        return CreateToken(payload);
    }

    public string Generate(string bucket, string email, string[] permissions, TimeSpan expiry, string? nonce = null)
    {
        var normalizedEmail = NormalizeEmail(email);
        var now = _timeProvider.GetUtcNow();

        var payload = new TokenPayload
        {
            Bucket = bucket,
            Email = normalizedEmail,
            Permissions = permissions,
            IssuedAt = now.ToUnixTimeSeconds(),
            ExpiresAt = now.Add(expiry).ToUnixTimeSeconds(),
            Nonce = nonce ?? GenerateNonce(),
            AllowReplay = true
        };

        return CreateToken(payload);
    }

    private string CreateToken(TokenPayload payload)
    {
        var payloadJson = JsonSerializer.Serialize(payload);
        var payloadBase64 = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));
        var signature = ComputeSignature(payloadBase64);

        return $"v1.{payloadBase64}.{signature}";
    }

    private string ComputeSignature(string payload)
    {
        using var hmac = new HMACSHA256(_signingKey);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Base64UrlEncode(hash);
    }

    private static string NormalizeEmail(string email)
    {
        return email.Trim().ToLowerInvariant();
    }

    private static string GenerateNonce()
    {
        var bytes = new byte[16];
        RandomNumberGenerator.Fill(bytes);
        return Base64UrlEncode(bytes);
    }

    private static string Base64UrlEncode(byte[] data)
    {
        return Convert.ToBase64String(data)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}

public sealed class GenerateTokenRequest
{
    /// <summary>
    /// Allow the token to be reused multiple times until expiry.
    /// Default: true (can revisit preference page multiple times).
    /// </summary>
    public bool AllowReplay { get; set; } = true;

    /// <summary>
    /// Token expiry in days from generation.
    /// Default: 60 days.
    /// </summary>
    public int ExpiryDays { get; set; } = 60;

    /// <summary>
    /// Language code for the preference page.
    /// Supported: "en", "de", "fr", "nl", "pl", "es".
    /// Default: "en" (English).
    /// </summary>
    public string Language { get; set; } = "en";
}
