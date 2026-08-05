using System.Security.Cryptography;

namespace Beacon.Core.Security;

/// <summary>Authenticated AES-256-GCM: nonce || ciphertext || tag.</summary>
internal static class AesGcmCipher
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    internal static byte[] Seal(byte[] plaintext, byte[] key)
    {
        var result = new byte[NonceSize + plaintext.Length + TagSize];
        var nonce = result.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(
            nonce,
            plaintext,
            result.AsSpan(NonceSize, plaintext.Length),
            result.AsSpan(NonceSize + plaintext.Length, TagSize));

        return result;
    }

    internal static byte[]? Open(byte[] sealedPayload, byte[] key)
    {
        if (sealedPayload.Length < NonceSize + TagSize)
            return null;

        var cipherLength = sealedPayload.Length - NonceSize - TagSize;
        var plaintext = new byte[cipherLength];

        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(
                sealedPayload.AsSpan(0, NonceSize),
                sealedPayload.AsSpan(NonceSize, cipherLength),
                sealedPayload.AsSpan(NonceSize + cipherLength, TagSize),
                plaintext);
            return plaintext;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}
