using Beacon.Core.Security;
using Xunit;

namespace Beacon.Tests;

public class SecurityTests
{
    private const string TestEncryptionKey = "MDEyMzQ1Njc4OTAxMjM0NTY3ODkwMTIzNDU2Nzg5MDE=";
    private const string TestPepper = "test-pepper-for-hashing";

    [Fact]
    public void Encryptor_EncryptDecrypt_RoundTrips()
    {
        var encryptor = new Encryptor(TestEncryptionKey);
        var plaintext = "sensitive data to encrypt";

        var encrypted = encryptor.Encrypt(plaintext);
        var decrypted = encryptor.Decrypt(encrypted);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Encryptor_ProducesDifferentCiphertextEachTime()
    {
        var encryptor = new Encryptor(TestEncryptionKey);
        var plaintext = "same input";

        var encrypted1 = encryptor.Encrypt(plaintext);
        var encrypted2 = encryptor.Encrypt(plaintext);

        Assert.NotEqual(encrypted1, encrypted2);
    }

    [Fact]
    public void Encryptor_ThrowsOnInvalidKeyLength()
    {
        var shortKey = Convert.ToBase64String(new byte[16]);

        Assert.Throws<ArgumentException>(() => new Encryptor(shortKey));
    }

    [Fact]
    public void EmailHasher_ProducesConsistentHash()
    {
        var hasher = new EmailHasher(TestPepper);

        var hash1 = hasher.Hash("test@example.com");
        var hash2 = hasher.Hash("test@example.com");

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void EmailHasher_NormalizesEmail()
    {
        var hasher = new EmailHasher(TestPepper);

        var hash1 = hasher.Hash("TEST@EXAMPLE.COM");
        var hash2 = hasher.Hash("  test@example.com  ");

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void EmailHasher_DifferentEmailsProduceDifferentHashes()
    {
        var hasher = new EmailHasher(TestPepper);

        var hash1 = hasher.Hash("user1@example.com");
        var hash2 = hasher.Hash("user2@example.com");

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void EmailHasher_DifferentPeppersProduceDifferentHashes()
    {
        var hasher1 = new EmailHasher("pepper1");
        var hasher2 = new EmailHasher("pepper2");
        var email = "test@example.com";

        var hash1 = hasher1.Hash(email);
        var hash2 = hasher2.Hash(email);

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void EmailHasher_ProducesHexString()
    {
        var hasher = new EmailHasher(TestPepper);

        var hash = hasher.Hash("test@example.com");

        Assert.Equal(64, hash.Length);
        Assert.True(hash.All(c => char.IsAsciiHexDigitLower(c)));
    }
}
