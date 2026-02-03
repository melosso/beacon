namespace Beacon.Core.Services;

public interface IEncryptionService
{
    string Encrypt(string plaintext);
    string Decrypt(string ciphertext);
    bool IsEncrypted(string value);
    string EncryptIfNotEncrypted(string value);
    string DecryptIfEncrypted(string value);
}
