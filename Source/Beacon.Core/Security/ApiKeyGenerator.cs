using System.Security.Cryptography;
using System.Text;

namespace Beacon.Core.Security;

public static class ApiKeyGenerator
{
    public static (string apiKey, string apiKeyHash) Generate()
    {
        var keyBytes = RandomNumberGenerator.GetBytes(32);
        var apiKey = Convert.ToBase64String(keyBytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        return (apiKey, ComputeHash(apiKey));
    }

    public static string ComputeHash(string apiKey)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(apiKey));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>Constant-time secret comparison. Hashes first so unequal lengths do not short-circuit.</summary>
    public static bool SecretEquals(string? provided, string? expected)
    {
        if (provided is null || expected is null)
            return false;

        return CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(provided)),
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
    }
}
