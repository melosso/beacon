using Beacon.Tokens;
using Xunit;

namespace Beacon.Tests;

public class TokenTests
{
    private const string TestSigningKey = "dGVzdC1zaWduaW5nLWtleS1mb3ItdGVzdGluZy1vbmx5LWtleQ==";
    private const string TestBucket = "test-bucket";

    private readonly TokenOptions _options = new()
    {
        SigningKey = TestSigningKey,
        ExpiryDays = 30
    };

    [Fact]
    public void Generate_CreatesValidToken()
    {
        var generator = new TokenGenerator(_options);
        var validator = new TokenValidator(_options);

        var token = generator.Generate(TestBucket, "test@example.com", ["newsletter"]);

        var result = validator.Validate(token);

        Assert.True(result.IsValid);
        Assert.NotNull(result.Payload);
        Assert.Equal(TestBucket, result.Payload.Bucket);
        Assert.Equal("test@example.com", result.Payload.Email);
        Assert.Single(result.Payload.Permissions);
        Assert.Equal("newsletter", result.Payload.Permissions[0]);
    }

    [Fact]
    public void Generate_NormalizesEmail()
    {
        var generator = new TokenGenerator(_options);
        var validator = new TokenValidator(_options);

        var token = generator.Generate(TestBucket, "  TEST@EXAMPLE.COM  ", ["alerts"]);

        var result = validator.Validate(token);

        Assert.True(result.IsValid);
        Assert.Equal("test@example.com", result.Payload!.Email);
    }

    [Fact]
    public void Validate_RejectsModifiedPayload()
    {
        var generator = new TokenGenerator(_options);
        var validator = new TokenValidator(_options);

        var token = generator.Generate(TestBucket, "test@example.com", ["newsletter"]);
        var parts = token.Split('.');
        parts[1] = "bW9kaWZpZWQtcGF5bG9hZA";
        var tamperedToken = string.Join('.', parts);

        var result = validator.Validate(tamperedToken);

        Assert.False(result.IsValid);
        Assert.Equal("Signature invalid", result.Error);
    }

    [Fact]
    public void Validate_RejectsExpiredToken()
    {
        var options = new TokenOptions
        {
            SigningKey = TestSigningKey,
            ExpiryDays = -1
        };
        var generator = new TokenGenerator(options);
        var validator = new TokenValidator(options);

        var token = generator.Generate(TestBucket, "test@example.com", ["newsletter"], TimeSpan.FromSeconds(-1));

        var result = validator.Validate(token);

        Assert.False(result.IsValid);
        Assert.True(result.IsExpired);
    }

    [Fact]
    public void Validate_RejectsInvalidFormat()
    {
        var validator = new TokenValidator(_options);

        var result = validator.Validate("invalid-token");

        Assert.False(result.IsValid);
        Assert.Equal("Token format invalid", result.Error);
    }

    [Fact]
    public void Validate_RejectsEmptyToken()
    {
        var validator = new TokenValidator(_options);

        var result = validator.Validate("");

        Assert.False(result.IsValid);
        Assert.Equal("Token is empty", result.Error);
    }

    [Fact]
    public void Validate_RejectsUnsupportedVersion()
    {
        var validator = new TokenValidator(_options);

        var result = validator.Validate("v3.payload.signature");

        Assert.False(result.IsValid);
        Assert.Equal("Unsupported token version", result.Error);
    }

    [Fact]
    public void SealedToken_RoundTripsAndHidesEmailFromPayload()
    {
        var sealedOptions = new TokenOptions
        {
            SigningKey = TestSigningKey,
            ExpiryDays = 30,
            PayloadEncryptionKey = Convert.ToBase64String(new byte[32])
        };

        var token = new TokenGenerator(sealedOptions).Generate(TestBucket, "test@example.com", ["newsletter"]);

        Assert.StartsWith("v2.", token);
        Assert.DoesNotContain("test@example.com", DecodePayloadSegment(token));

        var result = new TokenValidator(sealedOptions).Validate(token);

        Assert.True(result.IsValid);
        Assert.Equal("test@example.com", result.Payload!.Email);
        Assert.Equal(TestBucket, result.Payload.Bucket);
    }

    [Fact]
    public void SealedToken_CannotBeRelabelledAsV1()
    {
        var sealedOptions = new TokenOptions
        {
            SigningKey = TestSigningKey,
            PayloadEncryptionKey = Convert.ToBase64String(new byte[32])
        };

        var parts = new TokenGenerator(sealedOptions)
            .Generate(TestBucket, "test@example.com", ["newsletter"]).Split('.');

        var downgraded = $"v1.{parts[1]}.{parts[2]}";
        var result = new TokenValidator(sealedOptions).Validate(downgraded);

        Assert.False(result.IsValid);
        Assert.Equal("Signature invalid", result.Error);
    }

    [Fact]
    public void SealedToken_RejectedWhenPayloadKeyDiffers()
    {
        var issuerKey = new byte[32];
        var otherKey = new byte[32];
        otherKey[0] = 1;

        var issuer = new TokenOptions
        {
            SigningKey = TestSigningKey,
            PayloadEncryptionKey = Convert.ToBase64String(issuerKey)
        };
        var reader = new TokenOptions
        {
            SigningKey = TestSigningKey,
            PayloadEncryptionKey = Convert.ToBase64String(otherKey)
        };

        var token = new TokenGenerator(issuer).Generate(TestBucket, "test@example.com", ["newsletter"]);
        var result = new TokenValidator(reader).Validate(token);

        Assert.False(result.IsValid);
        Assert.Equal("Payload decode failed", result.Error);
    }

    [Fact]
    public void LegacyPlaintextTokens_StillValidateWhenSealingIsEnabled()
    {
        var legacyToken = new TokenGenerator(_options).Generate(TestBucket, "test@example.com", ["newsletter"]);

        var sealedOptions = new TokenOptions
        {
            SigningKey = TestSigningKey,
            PayloadEncryptionKey = Convert.ToBase64String(new byte[32])
        };

        var result = new TokenValidator(sealedOptions).Validate(legacyToken);

        Assert.True(result.IsValid);
        Assert.Equal("test@example.com", result.Payload!.Email);
    }

    private static string DecodePayloadSegment(string token)
    {
        var segment = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
        segment += (segment.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
        return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(segment));
    }

    [Fact]
    public void Generate_IncludesMultiplePermissions()
    {
        var generator = new TokenGenerator(_options);
        var validator = new TokenValidator(_options);

        var token = generator.Generate(TestBucket, "test@example.com", ["newsletter", "alerts", "updates"]);

        var result = validator.Validate(token);

        Assert.True(result.IsValid);
        Assert.Equal(3, result.Payload!.Permissions.Length);
        Assert.Contains("newsletter", result.Payload.Permissions);
        Assert.Contains("alerts", result.Payload.Permissions);
        Assert.Contains("updates", result.Payload.Permissions);
    }

    [Fact]
    public void Generate_WithSameInputs_ProducesDifferentTokens()
    {
        var generator = new TokenGenerator(_options);

        var token1 = generator.Generate(TestBucket, "test@example.com", ["newsletter"]);
        var token2 = generator.Generate(TestBucket, "test@example.com", ["newsletter"]);

        Assert.NotEqual(token1, token2);
    }

    [Fact]
    public void Generate_WithSameNonce_ProducesDifferentTokensDueToTimestamp()
    {
        var fakeTime = new FakeTimeProvider();
        var generator = new TokenGenerator(_options, fakeTime);
        var nonce = "test-nonce-12345";
        var expiry = TimeSpan.FromDays(7);

        var token1 = generator.Generate(TestBucket, "test@example.com", ["newsletter"], expiry, nonce);

        fakeTime.Advance(TimeSpan.FromSeconds(2));
        var token2 = generator.Generate(TestBucket, "test@example.com", ["newsletter"], expiry, nonce);

        // Even with same nonce, tokens differ due to different IssuedAt timestamps
        Assert.NotEqual(token1, token2);
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow = DateTimeOffset.UtcNow;
        public void Advance(TimeSpan delta) => _utcNow += delta;
        public override DateTimeOffset GetUtcNow() => _utcNow;
    }

    [Fact]
    public void Generate_WithAllowReplayFalse_SetsAllowReplayInPayload()
    {
        var generator = new TokenGenerator(_options);
        var validator = new TokenValidator(_options);

        var token = generator.Generate(TestBucket, "test@example.com", ["newsletter"], new GenerateTokenRequest
        {
            AllowReplay = false,
            ExpiryDays = 30
        });

        var result = validator.Validate(token);

        Assert.True(result.IsValid);
        Assert.NotNull(result.Payload);
        Assert.False(result.Payload.AllowReplay);
    }

    [Fact]
    public void Generate_WithCustomExpiryDays_SetsCorrectExpiry()
    {
        var generator = new TokenGenerator(_options);
        var validator = new TokenValidator(_options);

        var token = generator.Generate(TestBucket, "test@example.com", ["newsletter"], new GenerateTokenRequest
        {
            ExpiryDays = 90
        });

        var result = validator.Validate(token);

        Assert.True(result.IsValid);
        Assert.NotNull(result.Payload);
        // Check expiry is approximately 90 days from now
        var expectedExpiry = DateTimeOffset.UtcNow.AddDays(90);
        var actualExpiry = DateTimeOffset.FromUnixTimeSeconds(result.Payload.ExpiresAt);
        Assert.True(Math.Abs((expectedExpiry - actualExpiry).TotalMinutes) < 1);
    }

    [Fact]
    public void Generate_IncludesBucketInPayload()
    {
        var generator = new TokenGenerator(_options);
        var validator = new TokenValidator(_options);

        var token = generator.Generate("my-custom-bucket", "test@example.com", ["newsletter"]);

        var result = validator.Validate(token);

        Assert.True(result.IsValid);
        Assert.NotNull(result.Payload);
        Assert.Equal("my-custom-bucket", result.Payload.Bucket);
    }
}
