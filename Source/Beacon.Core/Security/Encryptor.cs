using System.Security.Cryptography;
using System.Text;

namespace Beacon.Core.Security;

public sealed class Encryptor
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly byte[] _key;

    public Encryptor(string base64Key)
    {
        _key = Convert.FromBase64String(base64Key);
        if (_key.Length != 32)
        {
            throw new ArgumentException("Encryption key must be 256 bits (32 bytes)");
        }
    }

    public string Encrypt(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
            return string.Empty;

        // Don't double encrypt
        if (IsEncrypted(plaintext))
            return plaintext;

        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = new byte[NonceSize];
        RandomNumberGenerator.Fill(nonce);

        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        var result = new byte[NonceSize + ciphertext.Length + TagSize];
        Buffer.BlockCopy(nonce, 0, result, 0, NonceSize);
        Buffer.BlockCopy(ciphertext, 0, result, NonceSize, ciphertext.Length);
        Buffer.BlockCopy(tag, 0, result, NonceSize + ciphertext.Length, TagSize);

        return "efx:" + Convert.ToBase64String(result);
    }

    public bool IsEncrypted(string? value)
    {
        return !string.IsNullOrEmpty(value) && value.StartsWith("efx:", StringComparison.Ordinal);
    }

    public string Decrypt(string encryptedBase64)
    {
        if (string.IsNullOrEmpty(encryptedBase64))
            return string.Empty;

        string actualBase64;
        bool hasPrefix = IsEncrypted(encryptedBase64);

        if (hasPrefix)
        {
            actualBase64 = encryptedBase64[4..];
        }
        else
        {
            actualBase64 = encryptedBase64;
        }

        try
        {
            var data = Convert.FromBase64String(actualBase64);

            if (data.Length < NonceSize + TagSize)
            {
                return encryptedBase64; // Too short to be our encrypted data
            }

            var nonce = new byte[NonceSize];
            var ciphertextLength = data.Length - NonceSize - TagSize;
            var ciphertext = new byte[ciphertextLength];
            var tag = new byte[TagSize];

            Buffer.BlockCopy(data, 0, nonce, 0, NonceSize);
            Buffer.BlockCopy(data, NonceSize, ciphertext, 0, ciphertextLength);
            Buffer.BlockCopy(data, NonceSize + ciphertextLength, tag, 0, TagSize);

            var plaintext = new byte[ciphertextLength];

            using var aes = new AesGcm(_key, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);

            return Encoding.UTF8.GetString(plaintext);
        }
        catch (Exception ex)
        {
            if (hasPrefix)
            {
                // The value was explicitly encrypted by this system (efx: prefix present).
                throw new CryptographicException(
                    "Failed to decrypt an 'efx:'-prefixed value. The encryption key may be incorrect or the stored data is corrupted.",
                    ex);
            }

            // No prefix: legacy / non-encrypted value. Return as-is for backwards compatibility.
            return encryptedBase64;
        }
    }
}
