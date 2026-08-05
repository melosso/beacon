using System.Text.Json;
using System.Text.Json.Nodes;
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
    /// Reads the sensitive configuration values, decrypting any that are already encrypted.
    /// Writes nothing: call <see cref="PersistEncrypted"/> only once the values have been validated,
    /// so a startup that is going to be rejected does not first overwrite appsettings.json with
    /// ciphertext the operator cannot read.
    /// </summary>
    public static Dictionary<string, string> ReadSensitiveValues(
        IConfiguration configuration,
        EncryptionService encryptionService)
    {
        var values = new Dictionary<string, string>();
        var config = configuration.GetSection("Beacon");

        foreach (var key in SensitiveKeys)
        {
            var value = config[key];
            if (string.IsNullOrEmpty(value))
                continue;

            values[key] = encryptionService.IsEncrypted(value)
                ? encryptionService.Decrypt(value)
                : value;
        }

        return values;
    }

    /// <summary>Encrypts any still-plaintext sensitive values in appsettings.json.</summary>
    public static void PersistEncrypted(
        string contentRootPath,
        EncryptionService encryptionService,
        Dictionary<string, string> plaintextValues)
    {
        if (plaintextValues.Values.Any(v => !encryptionService.IsEncrypted(v)))
            EncryptAppSettings(contentRootPath, encryptionService, plaintextValues);
    }

    private static void EncryptAppSettings(
        string contentRootPath,
        EncryptionService encryptionService,
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
            var root = JsonNode.Parse(
                File.ReadAllText(appSettingsPath),
                nodeOptions: null,
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

            if (root?["Beacon"] is not JsonObject beacon)
            {
                Log.Warning("appsettings.json has no Beacon section, cannot encrypt configuration");
                return;
            }

            var encryptedCount = 0;
            foreach (var key in SensitiveKeys)
            {
                if (!plaintextValues.TryGetValue(key, out var plaintext) || encryptionService.IsEncrypted(plaintext))
                    continue;

                if (beacon[key]?.GetValue<string>() is not { } fileValue || fileValue.Length == 0)
                    continue;

                // Never write a value that an environment variable or .env supplied.
                if (fileValue != plaintext)
                    continue;

                beacon[key] = encryptionService.Encrypt(plaintext);
                encryptedCount++;
            }

            if (encryptedCount > 0)
            {
                File.WriteAllText(appSettingsPath,
                    root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                Log.Debug("Encrypted {Count} configuration value(s) in appsettings.json", encryptedCount);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to encrypt appsettings.json");
        }
    }

    /// <summary>
    /// Decrypts a single configuration value if it's encrypted.
    /// </summary>
    public static string DecryptValue(string? value, EncryptionService encryptionService)
    {
        if (string.IsNullOrEmpty(value))
            return value ?? string.Empty;

        return encryptionService.DecryptIfEncrypted(value);
    }
}
