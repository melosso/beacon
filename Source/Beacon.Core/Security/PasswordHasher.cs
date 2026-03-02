using System.Security.Cryptography;
using System.Text;

namespace Beacon.Core.Security;

public static class PasswordHasher
{
    private const int Iterations = 600_000;
    private const int SaltSize = 32;
    private const int HashSize = 64;

    public static (string hash, string salt) HashPassword(string password)
    {
        var saltBytes = RandomNumberGenerator.GetBytes(SaltSize);
        var hashBytes = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            saltBytes,
            Iterations,
            HashAlgorithmName.SHA512,
            HashSize);

        return (Convert.ToBase64String(hashBytes), Convert.ToBase64String(saltBytes));
    }

    public static bool VerifyPassword(string password, string storedHash, string storedSalt)
    {
        var saltBytes = Convert.FromBase64String(storedSalt);
        var hashBytes = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            saltBytes,
            Iterations,
            HashAlgorithmName.SHA512,
            HashSize);

        var storedHashBytes = Convert.FromBase64String(storedHash);
        return CryptographicOperations.FixedTimeEquals(hashBytes, storedHashBytes);
    }
}
