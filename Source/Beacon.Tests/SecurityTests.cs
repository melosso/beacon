using Beacon.Core.Security;
using Beacon.Core.Services;
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
    public void Encryptor_Decrypt_ReturnsRawString_OnInvalidBase64()
    {
        var encryptor = new Encryptor(TestEncryptionKey);
        var rawApiKey = "re_123456789"; // Not valid base64 due to underscore

        var result = encryptor.Decrypt(rawApiKey);

        Assert.Equal(rawApiKey, result);
    }

    [Fact]
    public void Encryptor_IsEncrypted_Works()
    {
        var encryptor = new Encryptor(TestEncryptionKey);
        var plaintext = "test input";
        var encrypted = encryptor.Encrypt(plaintext);

        Assert.True(encryptor.IsEncrypted(encrypted));
        Assert.False(encryptor.IsEncrypted(plaintext));
    }

    [Fact]
    public void Encryptor_Decrypt_HandlesLegacyUnprefixedData()
    {
        // Setup a legacy-style encrypted string (no "efx:" prefix)
        var key = Convert.FromBase64String(TestEncryptionKey);
        var plaintext = "legacy secret";
        var plaintextBytes = System.Text.Encoding.UTF8.GetBytes(plaintext);
        var nonce = new byte[12];
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[16];
        using var aes = new System.Security.Cryptography.AesGcm(key, 16);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);
        var result = new byte[12 + plaintextBytes.Length + 16];
        Buffer.BlockCopy(nonce, 0, result, 0, 12);
        Buffer.BlockCopy(ciphertext, 0, result, 12, plaintextBytes.Length);
        Buffer.BlockCopy(tag, 0, result, 12 + plaintextBytes.Length, 16);
        var legacyEncrypted = Convert.ToBase64String(result);

        var encryptor = new Encryptor(TestEncryptionKey);
        var decrypted = encryptor.Decrypt(legacyEncrypted);

        Assert.Equal(plaintext, decrypted);
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

    private static EncryptionService CreateEncryptionService(string testDir)
    {
        // Create test directory with appsettings.json so it's recognized as root
        Directory.CreateDirectory(testDir);
        File.WriteAllText(Path.Combine(testDir, "appsettings.json"), "{}");
        return new EncryptionService(testDir, "test-encryption-key-for-unit-tests");
    }

    private static void CleanupTestDir(string testDir)
    {
        if (Directory.Exists(testDir))
        {
            Directory.Delete(testDir, recursive: true);
        }
    }

    [Fact]
    public void EncryptionService_EncryptDecrypt_RoundTrips()
    {
        var testDir = Path.Combine(Path.GetTempPath(), $"beacon-test-{Guid.NewGuid()}");
        try
        {
            var service = CreateEncryptionService(testDir);
            var plaintext = "sensitive connection string";

            var encrypted = service.Encrypt(plaintext);
            var decrypted = service.Decrypt(encrypted);

            Assert.Equal(plaintext, decrypted);
        }
        finally
        {
            CleanupTestDir(testDir);
        }
    }

    [Fact]
    public void EncryptionService_AddsPrefixToEncryptedValues()
    {
        var testDir = Path.Combine(Path.GetTempPath(), $"beacon-test-{Guid.NewGuid()}");
        try
        {
            var service = CreateEncryptionService(testDir);

            var encrypted = service.Encrypt("test");

            Assert.StartsWith("BENC:", encrypted);
        }
        finally
        {
            CleanupTestDir(testDir);
        }
    }

    [Fact]
    public void EncryptionService_IsEncrypted_ReturnsTrueForEncryptedValues()
    {
        var testDir = Path.Combine(Path.GetTempPath(), $"beacon-test-{Guid.NewGuid()}");
        try
        {
            var service = CreateEncryptionService(testDir);

            var encrypted = service.Encrypt("test");

            Assert.True(service.IsEncrypted(encrypted));
        }
        finally
        {
            CleanupTestDir(testDir);
        }
    }

    [Fact]
    public void EncryptionService_IsEncrypted_ReturnsFalseForPlaintext()
    {
        var testDir = Path.Combine(Path.GetTempPath(), $"beacon-test-{Guid.NewGuid()}");
        try
        {
            var service = CreateEncryptionService(testDir);

            Assert.False(service.IsEncrypted("plaintext"));
            Assert.False(service.IsEncrypted(""));
            Assert.False(service.IsEncrypted(null!));
        }
        finally
        {
            CleanupTestDir(testDir);
        }
    }

    [Fact]
    public void EncryptionService_EncryptIfNotEncrypted_SkipsAlreadyEncrypted()
    {
        var testDir = Path.Combine(Path.GetTempPath(), $"beacon-test-{Guid.NewGuid()}");
        try
        {
            var service = CreateEncryptionService(testDir);

            var encrypted = service.Encrypt("test");
            var result = service.EncryptIfNotEncrypted(encrypted);

            Assert.Equal(encrypted, result);
        }
        finally
        {
            CleanupTestDir(testDir);
        }
    }

    [Fact]
    public void EncryptionService_EncryptIfNotEncrypted_EncryptsPlaintext()
    {
        var testDir = Path.Combine(Path.GetTempPath(), $"beacon-test-{Guid.NewGuid()}");
        try
        {
            var service = CreateEncryptionService(testDir);

            var result = service.EncryptIfNotEncrypted("plaintext");

            Assert.True(service.IsEncrypted(result));
        }
        finally
        {
            CleanupTestDir(testDir);
        }
    }

    [Fact]
    public void EncryptionService_DecryptIfEncrypted_DecryptsEncrypted()
    {
        var testDir = Path.Combine(Path.GetTempPath(), $"beacon-test-{Guid.NewGuid()}");
        try
        {
            var service = CreateEncryptionService(testDir);

            var encrypted = service.Encrypt("secret");
            var result = service.DecryptIfEncrypted(encrypted);

            Assert.Equal("secret", result);
        }
        finally
        {
            CleanupTestDir(testDir);
        }
    }

    [Fact]
    public void EncryptionService_DecryptIfEncrypted_ReturnsPlaintextUnchanged()
    {
        var testDir = Path.Combine(Path.GetTempPath(), $"beacon-test-{Guid.NewGuid()}");
        try
        {
            var service = CreateEncryptionService(testDir);

            var result = service.DecryptIfEncrypted("not encrypted");

            Assert.Equal("not encrypted", result);
        }
        finally
        {
            CleanupTestDir(testDir);
        }
    }

    [Fact]
    public void EncryptionService_Decrypt_ThrowsOnNonEncryptedValue()
    {
        var testDir = Path.Combine(Path.GetTempPath(), $"beacon-test-{Guid.NewGuid()}");
        try
        {
            var service = CreateEncryptionService(testDir);

            Assert.Throws<ArgumentException>(() => service.Decrypt("not encrypted"));
        }
        finally
        {
            CleanupTestDir(testDir);
        }
    }

    [Fact]
    public void EncryptionService_HandlesEmptyStrings()
    {
        var testDir = Path.Combine(Path.GetTempPath(), $"beacon-test-{Guid.NewGuid()}");
        try
        {
            var service = CreateEncryptionService(testDir);

            Assert.Equal("", service.Encrypt(""));
            Assert.Equal("", service.Decrypt(""));
            Assert.Equal("", service.EncryptIfNotEncrypted(""));
            Assert.Equal("", service.DecryptIfEncrypted(""));
        }
        finally
        {
            CleanupTestDir(testDir);
        }
    }

    [Fact]
    public void EncryptionService_CreatesCoreFolder()
    {
        var testDir = Path.Combine(Path.GetTempPath(), $"beacon-test-{Guid.NewGuid()}");
        try
        {
            var service = CreateEncryptionService(testDir);

            Assert.True(Directory.Exists(service.CertificatesPath));
            Assert.True(File.Exists(Path.Combine(service.CertificatesPath, "recovery.baklz4")));
            Assert.True(File.Exists(Path.Combine(service.CertificatesPath, "snapshot_blob.bin")));
            Assert.True(File.Exists(Path.Combine(service.CertificatesPath, "store.jsonc")));
        }
        finally
        {
            CleanupTestDir(testDir);
        }
    }

    [Fact]
    public void EncryptionService_ReusesExistingKeys()
    {
        var testDir = Path.Combine(Path.GetTempPath(), $"beacon-test-{Guid.NewGuid()}");
        try
        {
            var service1 = CreateEncryptionService(testDir);
            var encrypted = service1.Encrypt("test data");

            // Create new service instance with same directory
            var service2 = new EncryptionService(testDir, "test-encryption-key-for-unit-tests");
            var decrypted = service2.Decrypt(encrypted);

            Assert.Equal("test data", decrypted);
        }
        finally
        {
            CleanupTestDir(testDir);
        }
    }

    [Fact]
    public void EncryptionService_ThrowsOnKeyChange()
    {
        var testDir = Path.Combine(Path.GetTempPath(), $"beacon-test-{Guid.NewGuid()}");
        try
        {
            _ = CreateEncryptionService(testDir);

            // Try to create new service with different key
            Assert.Throws<InvalidOperationException>(() =>
                new EncryptionService(testDir, "different-encryption-key"));
        }
        finally
        {
            CleanupTestDir(testDir);
        }
    }
}
