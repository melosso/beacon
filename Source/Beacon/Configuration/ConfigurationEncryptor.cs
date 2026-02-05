using Beacon.Core.Services;
using Serilog;

namespace Beacon.Configuration;

/// <summary>
/// Encrypts sensitive configuration values in appsettings.json on first run
/// and decrypts them when reading configuration.
/// </summary>
public static class ConfigurationEncryptor
{
    private static readonly string[] SensitiveKeys =
    [
        "SigningKey",
        "EncryptionKey",
        "Pepper",
        "AdminApiKey",
        "ConnectionString"
    ];

    /// <summary>
    /// Ensures sensitive configuration values are encrypted in appsettings.json.
    /// Returns decrypted values for use in the application.
    /// </summary>
    public static Dictionary<string, string> ProcessConfiguration(
        IConfiguration configuration,
        IEncryptionService encryptionService,
        string contentRootPath)
    {
        var decryptedValues = new Dictionary<string, string>();
        var config = configuration.GetSection("Beacon");
        var needsEncryption = false;

        // Check which values need encryption
        foreach (var key in SensitiveKeys)
        {
            var value = config[key];
            if (string.IsNullOrEmpty(value))
                continue;

            if (encryptionService.IsEncrypted(value))
            {
                // Already encrypted - decrypt for use
                decryptedValues[key] = encryptionService.Decrypt(value);
            }
            else
            {
                // Not encrypted - will need to encrypt
                decryptedValues[key] = value;
                needsEncryption = true;
            }
        }

        // Encrypt values in appsettings.json if needed
        if (needsEncryption)
        {
            EncryptAppSettings(contentRootPath, encryptionService, decryptedValues);
        }

        return decryptedValues;
    }

    private static void EncryptAppSettings(
        string contentRootPath,
        IEncryptionService encryptionService,
        Dictionary<string, string> plaintextValues)
    {
        var appSettingsPath = Path.Combine(contentRootPath, "appsettings.json");
        if (!File.Exists(appSettingsPath))
        {
            Log.Warning("appsettings.json not found, cannot encrypt configuration");
            return;
        }

        try
        {
            var json = File.ReadAllText(appSettingsPath);
            var encryptedCount = 0;

            // Use regex-based replacement to preserve comments and formatting
            foreach (var key in SensitiveKeys)
            {
                if (!plaintextValues.TryGetValue(key, out var plaintext))
                    continue;

                // Skip if already encrypted
                if (encryptionService.IsEncrypted(plaintext))
                    continue;

                var encrypted = encryptionService.Encrypt(plaintext);

                // Escape the plaintext for regex (handle special characters)
                var escapedPlaintext = System.Text.RegularExpressions.Regex.Escape(plaintext);

                // Match the key-value pattern: "Key": "value"
                var pattern = $@"(""{key}""\s*:\s*""){escapedPlaintext}("")";
                var replacement = $"$1{EscapeJsonString(encrypted)}$2";

                var newJson = System.Text.RegularExpressions.Regex.Replace(json, pattern, replacement);
                if (newJson != json)
                {
                    json = newJson;
                    encryptedCount++;
                }
            }

            if (encryptedCount > 0)
            {
                File.WriteAllText(appSettingsPath, json);
                Log.Debug($"Encrypted {encryptedCount} configuration value(s) in appsettings.json");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to encrypt appsettings.json: {ex.Message}");
        }
    }

    private static string EscapeJsonString(string value)
    {
        // Escape special JSON characters
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }

    /// <summary>
    /// Decrypts a single configuration value if it's encrypted.
    /// </summary>
    public static string DecryptValue(string? value, IEncryptionService encryptionService)
    {
        if (string.IsNullOrEmpty(value))
            return value ?? string.Empty;

        return encryptionService.DecryptIfEncrypted(value);
    }
}
