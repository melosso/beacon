using System.Security.Cryptography;
using Beacon.Core.Security;
using System.Text;
using System.Text.Json;
using Serilog;

namespace Beacon.Core.Services;

/// <summary>
/// Hybrid RSA+AES encryption service for securing sensitive data
/// - RSA 2048-bit for key encryption
/// - AES 256-bit for data encryption
/// - Encrypted content prefix: "BENC:"
/// </summary>
public class EncryptionService {
    private const string LegacyHeader = "BENC:";
    private const string EncryptedHeader = "BENC2:";
    private const string PrivateKeyFileName = "recovery.baklz4";
    private readonly string _certsPath;

    private const string GeneratedKeyFileName = "instance.key";

    // Shipped in every binary, so anything encrypted under it is effectively plaintext. Kept only to
    // read data written by versions that defaulted to it; never used for a fresh install.
    private const string LegacyFallbackKey = "$BEACON2.0_FallbackEncryptionKey_ChangeInProduction_MinLength32Chars#";

    private readonly string _encryptionKey;

    public EncryptionService(string rootPath)
        : this(rootPath, null) { }

    public EncryptionService(string rootPath, string? encryptionKey)
    {
        _certsPath = InitializeCoreDirectory(rootPath);
        _encryptionKey = encryptionKey ?? LoadEncryptionKey();
    }

    /// <summary>
    /// Creates and hides the .core directory for storing encryption keys.
    /// On Windows, sets the Hidden attribute. On Unix, the dot prefix is sufficient.
    /// </summary>
    private static string InitializeCoreDirectory(string rootPath)
    {
        var projectRoot = FindProjectRoot(rootPath);
        var certsPath = Path.Combine(projectRoot, ".core");
        Directory.CreateDirectory(certsPath);

        // Hide .core directory on Windows (Unix hides dot-prefixed directories by default)
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var dirInfo = new DirectoryInfo(certsPath);
                dirInfo.Attributes |= FileAttributes.Hidden;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Unable to set hidden attribute on .core directory; continuing");
            }
        }

        return certsPath;
    }

    public string Encrypt(string plainText)
    {
        ArgumentNullException.ThrowIfNull(plainText);
        if (string.IsNullOrEmpty(plainText))
            return plainText;

        var sealedBytes = AesGcmCipher.Seal(Encoding.UTF8.GetBytes(plainText), DeriveKeyFromPassword(_encryptionKey));
        return EncryptedHeader + Convert.ToBase64String(sealedBytes);
    }

    public string Decrypt(string encryptedContent)
    {
        ArgumentNullException.ThrowIfNull(encryptedContent);
        if (string.IsNullOrEmpty(encryptedContent))
            return encryptedContent;

        if (encryptedContent.StartsWith(EncryptedHeader, StringComparison.Ordinal))
        {
            var payload = Convert.FromBase64String(encryptedContent[EncryptedHeader.Length..]);
            var plain = AesGcmCipher.Open(payload, DeriveKeyFromPassword(_encryptionKey))
                ?? throw new CryptographicException(
                    "Failed to decrypt configuration value. BEACON_ENCRYPTION_KEY may have changed.");
            return Encoding.UTF8.GetString(plain);
        }

        if (encryptedContent.StartsWith(LegacyHeader, StringComparison.Ordinal))
            return DecryptLegacy(encryptedContent);

        throw new ArgumentException("Value is not encrypted (missing prefix)", nameof(encryptedContent));
    }

    public bool IsEncrypted(string value) =>
        !string.IsNullOrEmpty(value) &&
        (value.StartsWith(EncryptedHeader, StringComparison.Ordinal) ||
         value.StartsWith(LegacyHeader, StringComparison.Ordinal));

    /// <summary>Reads values written by the pre-AES-GCM hybrid RSA format. Re-saving rewrites them.</summary>
    private string DecryptLegacy(string encryptedContent)
    {
        var privateKeyPath = Path.Combine(_certsPath, PrivateKeyFileName);
        if (!File.Exists(privateKeyPath))
            throw new InvalidOperationException("Private key not found. Required to read legacy BENC: values.");

        return DecryptWithPrivateKey(encryptedContent, DecryptPrivateKey(File.ReadAllText(privateKeyPath)));
    }

    /// <summary>
    /// Encrypt only if not already encrypted
    /// </summary>
    public string EncryptIfNotEncrypted(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        return IsEncrypted(value) ? value : Encrypt(value);
    }

    /// <summary>
    /// Decrypt only if encrypted
    /// </summary>
    public string DecryptIfEncrypted(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        return IsEncrypted(value) ? Decrypt(value) : value;
    }

    /// <summary>
    /// Get the path to the .core certificates folder
    /// </summary>
    public string CertificatesPath => _certsPath;

    private static readonly string[] KeyNames = ["BEACON_ENCRYPTION_KEY", "Beacon__EncryptionKey"];

    /// <summary>Environment first, then a .env beside the binary, then a generated per-instance key.</summary>
    private string LoadEncryptionKey()
    {
        foreach (var name in KeyNames)
        {
            if (Environment.GetEnvironmentVariable(name) is { Length: > 0 } value)
                return value;
        }

        if (ReadEnvFile() is { } fromFile)
            return fromFile;

        return LoadOrCreateInstanceKey();
    }

    private static string? ReadEnvFile()
    {
        var path = Path.Combine(FindProjectRoot(AppContext.BaseDirectory), ".env");
        if (!File.Exists(path))
            return null;

        try
        {
            foreach (var line in File.ReadLines(path))
            {
                var parts = line.Trim().Split('=', 2);
                if (parts.Length == 2 && KeyNames.Contains(parts[0].Trim()))
                {
                    var value = parts[1].Trim().Trim('"', '\'');
                    if (value.Length > 0)
                        return value;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to read .env file; continuing key discovery");
        }

        return null;
    }

    private string LoadOrCreateInstanceKey()
    {
        var keyPath = Path.Combine(_certsPath, GeneratedKeyFileName);

        if (File.Exists(keyPath))
        {
            var stored = File.ReadAllText(keyPath).Trim();
            if (!string.IsNullOrWhiteSpace(stored))
                return stored;
        }

        // Pre-existing install whose data was encrypted under the shipped constant. Keep it readable.
        if (File.Exists(Path.Combine(_certsPath, PrivateKeyFileName)))
        {
            Log.Warning("BEACON_ENCRYPTION_KEY is not set; using the legacy fallback key, which ships in every binary. Set it and re-enter your secrets to rotate.");
            return LegacyFallbackKey;
        }

        var generated = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        File.WriteAllText(keyPath, generated);
        RestrictToOwner(keyPath);

        Log.Warning("BEACON_ENCRYPTION_KEY is not set; generated one at {KeyPath}. Back up .core or lose access to encrypted data.", keyPath);

        return generated;
    }

    private static void RestrictToOwner(string path)
    {
        if (OperatingSystem.IsWindows()) return;
        try { File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
        catch (Exception ex) { Log.Debug(ex, "Could not restrict permissions on {Path}", path); }
    }

    /// <summary>
    /// Find project root by looking for .env file or Beacon.db
    /// </summary>
    public static string FindProjectRoot(string startPath)
    {
        var current = new DirectoryInfo(startPath);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, ".env")) ||
                File.Exists(Path.Combine(current.FullName, "Beacon.db")) ||
                File.Exists(Path.Combine(current.FullName, "appsettings.json")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }
        return startPath;
    }

    /// <summary>
    /// AES + RSA hybrid decryption
    /// </summary>
    private static string DecryptWithPrivateKey(string encryptedContent, string privateKeyPem)
    {
        if (!encryptedContent.StartsWith(EncryptedHeader))
            throw new InvalidOperationException("Content is not encrypted");

        var payload = encryptedContent[EncryptedHeader.Length..];
        var parts = payload.Split("::", StringSplitOptions.None);
        if (parts.Length != 2)
            throw new FormatException("Invalid encrypted format");

        var encryptedKeyIv = Convert.FromBase64String(parts[0]);
        var cipherBytes = Convert.FromBase64String(parts[1]);

        var sanitizedPem = string.Join("\n",
            privateKeyPem.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim()));

        using var rsa = RSA.Create();
        rsa.ImportFromPem(sanitizedPem);
        var keyIv = rsa.Decrypt(encryptedKeyIv, RSAEncryptionPadding.OaepSHA256);

        var key = new byte[32];
        var iv = new byte[16];
        Buffer.BlockCopy(keyIv, 0, key, 0, 32);
        Buffer.BlockCopy(keyIv, 32, iv, 0, 16);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        using var ms = new MemoryStream(cipherBytes);
        using var cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Read);
        using var sr = new StreamReader(cs, Encoding.UTF8);
        return sr.ReadToEnd();
    }

    /// <summary>
    /// Decrypt private key using master encryption key
    /// </summary>
    private string DecryptPrivateKey(string encrypted)
    {
        var bytes = Convert.FromBase64String(encrypted);
        using var ms = new MemoryStream(bytes);
        var iv = new byte[16];
        ms.Read(iv, 0, 16);
        using var aes = Aes.Create();
        aes.Key = DeriveKeyFromPassword(_encryptionKey);
        aes.IV = iv;
        using var cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Read);
        using var sr = new StreamReader(cs);
        return sr.ReadToEnd();
    }

    /// <summary>
    /// Derive a 256-bit key from a password string using SHA256
    /// </summary>
    private static byte[] DeriveKeyFromPassword(string password)
    {
        return SHA256.HashData(Encoding.UTF8.GetBytes(password));
    }
}
