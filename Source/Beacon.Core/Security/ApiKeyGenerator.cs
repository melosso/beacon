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
}
