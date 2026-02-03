using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Beacon.Core.Services;

/// <summary>
/// Hybrid RSA+AES encryption service for securing sensitive data
/// - RSA 2048-bit for key encryption
/// - AES 256-bit for data encryption
/// - Encrypted content prefix: "BENC:"
/// </summary>
public class EncryptionService : IEncryptionService
{
    private const string EncryptedHeader = "BENC:";
    private const string PrivateKeyFileName = "recovery.baklz4";
    private const string PublicKeyFileName = "snapshot_blob.bin";
    private readonly string _certsPath;
    private string _currentPublicKeyPem = string.Empty;

    private const string FallbackKey = "$BEACON2.0_FallbackEncryptionKey_ChangeInProduction_MinLength32Chars#";

    private readonly string _encryptionKey;

    public EncryptionService(string rootPath)
    {
        _certsPath = InitializeCoreDirectory(rootPath);
        _encryptionKey = LoadEncryptionKey();
        InitializeKeyPair();
    }

    public EncryptionService(string rootPath, string encryptionKey)
    {
        _certsPath = InitializeCoreDirectory(rootPath);
        _encryptionKey = encryptionKey;
        InitializeKeyPair();
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
            catch
            {
                // Ignore if unable to set hidden attribute (e.g., permissions)
            }
        }

        return certsPath;
    }

    /// <summary>
    /// Encrypt plaintext using hybrid RSA+AES encryption
    /// </summary>
    public string Encrypt(string plainText)
    {
        ArgumentNullException.ThrowIfNull(plainText);
        if (string.IsNullOrEmpty(plainText))
            return plainText;

        return EncryptWithPublicKey(plainText, _currentPublicKeyPem);
    }

    /// <summary>
    /// Decrypt encrypted content using private key
    /// </summary>
    public string Decrypt(string encryptedContent)
    {
        ArgumentNullException.ThrowIfNull(encryptedContent);
        if (string.IsNullOrEmpty(encryptedContent))
            return encryptedContent;

        if (!IsEncrypted(encryptedContent))
        {
            throw new ArgumentException("Value is not encrypted (missing prefix)", nameof(encryptedContent));
        }

        var privateKeyPath = Path.Combine(_certsPath, PrivateKeyFileName);
        if (!File.Exists(privateKeyPath))
        {
            throw new InvalidOperationException("Private key not found. Required for decryption.");
        }

        var encrypted = File.ReadAllText(privateKeyPath);
        var privateKey = DecryptPrivateKey(encrypted);
        return DecryptWithPrivateKey(encryptedContent, privateKey);
    }

    /// <summary>
    /// Check if content is encrypted
    /// </summary>
    public bool IsEncrypted(string value)
    {
        return !string.IsNullOrEmpty(value) && value.StartsWith(EncryptedHeader, StringComparison.Ordinal);
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

    /// <summary>
    /// Load encryption key from various sources (priority order)
    /// </summary>
    private string LoadEncryptionKey()
    {
        // Priority 1: Check system environment variable (Windows)
        if (OperatingSystem.IsWindows())
        {
            var envKey = Environment.GetEnvironmentVariable("BEACON_ENCRYPTION_KEY", EnvironmentVariableTarget.Machine);
            if (!string.IsNullOrWhiteSpace(envKey))
            {
                return envKey;
            }
        }

        // Priority 2: Check process environment variable
        var processEnvKey = Environment.GetEnvironmentVariable("BEACON_ENCRYPTION_KEY", EnvironmentVariableTarget.Process);
        if (!string.IsNullOrWhiteSpace(processEnvKey))
        {
            return processEnvKey;
        }

        // Priority 3: Check Beacon__EncryptionKey (ASP.NET Core configuration style)
        var beaconEnvKey = Environment.GetEnvironmentVariable("Beacon__EncryptionKey");
        if (!string.IsNullOrWhiteSpace(beaconEnvKey))
        {
            return beaconEnvKey;
        }

        // Priority 4: Check .env file
        var projectRoot = FindProjectRoot(AppContext.BaseDirectory);
        var envFilePath = Path.Combine(projectRoot, ".env");

        if (File.Exists(envFilePath))
        {
            try
            {
                var envLines = File.ReadAllLines(envFilePath);
                foreach (var line in envLines)
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith('#') || string.IsNullOrWhiteSpace(trimmed))
                        continue;

                    var parts = trimmed.Split('=', 2);
                    if (parts.Length == 2)
                    {
                        var key = parts[0].Trim();
                        if (key is "BEACON_ENCRYPTION_KEY" or "Beacon__EncryptionKey")
                        {
                            var value = parts[1].Trim().Trim('"', '\'');
                            if (!string.IsNullOrWhiteSpace(value))
                            {
                                return value;
                            }
                        }
                    }
                }
            }
            catch
            {
                // Ignore .env read errors
            }
        }

        // Priority 5: Fallback key (with console warning)
        Console.WriteLine("WARNING: No BEACON_ENCRYPTION_KEY found. Using fallback key.");
        Console.WriteLine("WARNING: For production, set BEACON_ENCRYPTION_KEY environment variable.");

        return FallbackKey;
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
    /// Initialize or load RSA key pair
    /// </summary>
    private void InitializeKeyPair()
    {
        var privateKeyPath = Path.Combine(_certsPath, PrivateKeyFileName);
        var publicKeyPath = Path.Combine(_certsPath, PublicKeyFileName);

        if (!File.Exists(privateKeyPath))
        {
            // Generate new keypair
            using var rsa = RSA.Create(2048);
            var privateKeyPem = ExportPrivateKeyPem(rsa);
            var publicKeyPem = ExportPublicKeyPem(rsa);

            // Save private key (encrypted with master encryption key)
            File.WriteAllText(privateKeyPath, EncryptPrivateKey(privateKeyPem));

            // Save public key
            File.WriteAllText(publicKeyPath, publicKeyPem);

            // Save reference file
            var referencePath = Path.Combine(_certsPath, "store.jsonc");
            var machine = Environment.MachineName;
            var timestamp = DateTimeOffset.Now.ToString("o"); // ISO 8601

            var referenceContent = new
            {
                MachineIdentity = Convert.ToBase64String(Encoding.UTF8.GetBytes(machine)),
                Timestamp = timestamp
            };
            File.WriteAllText(referencePath, JsonSerializer.Serialize(referenceContent, new JsonSerializerOptions { WriteIndented = true }));

            _currentPublicKeyPem = publicKeyPem;
            Console.WriteLine("Generated new RSA keypair for encryption in .core folder");
        }
        else
        {
            // Private key exists, derive public key from it
            try
            {
                var encrypted = File.ReadAllText(privateKeyPath);
                var privateKeyPem = DecryptPrivateKey(encrypted);
                using var rsa = RSA.Create();
                rsa.ImportFromPem(privateKeyPem);
                var derivedPublicKeyPem = ExportPublicKeyPem(rsa);
                _currentPublicKeyPem = derivedPublicKeyPem;

                // Also save/update public key file
                File.WriteAllText(publicKeyPath, derivedPublicKeyPem);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "Failed to decrypt private key. If you changed BEACON_ENCRYPTION_KEY, " +
                    "you must delete the .core folder to regenerate keys.", ex);
            }
        }
    }

    /// <summary>
    /// AES + RSA hybrid encryption
    /// </summary>
    private static string EncryptWithPublicKey(string plainText, string publicKeyPem)
    {
        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.GenerateKey();
        aes.GenerateIV();

        byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
        byte[] cipherBytes;
        using (var ms = new MemoryStream())
        using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
        {
            cs.Write(plainBytes, 0, plainBytes.Length);
            cs.FlushFinalBlock();
            cipherBytes = ms.ToArray();
        }

        var keyIv = new byte[aes.Key.Length + aes.IV.Length];
        Buffer.BlockCopy(aes.Key, 0, keyIv, 0, aes.Key.Length);
        Buffer.BlockCopy(aes.IV, 0, keyIv, aes.Key.Length, aes.IV.Length);

        using var rsa = RSA.Create();
        rsa.ImportFromPem(publicKeyPem);
        var encryptedKeyIv = rsa.Encrypt(keyIv, RSAEncryptionPadding.OaepSHA256);

        return EncryptedHeader + Convert.ToBase64String(encryptedKeyIv) + "::" + Convert.ToBase64String(cipherBytes);
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
    /// Export private key as PEM format
    /// </summary>
    private static string ExportPrivateKeyPem(RSA rsa)
    {
        var builder = new StringBuilder();
        builder.AppendLine("-----BEGIN PRIVATE KEY-----");
        builder.AppendLine(Convert.ToBase64String(rsa.ExportPkcs8PrivateKey(), Base64FormattingOptions.InsertLineBreaks));
        builder.AppendLine("-----END PRIVATE KEY-----");
        return builder.ToString();
    }

    /// <summary>
    /// Export public key as PEM format
    /// </summary>
    private static string ExportPublicKeyPem(RSA rsa)
    {
        var builder = new StringBuilder();
        builder.AppendLine("-----BEGIN PUBLIC KEY-----");
        builder.AppendLine(Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo(), Base64FormattingOptions.InsertLineBreaks));
        builder.AppendLine("-----END PUBLIC KEY-----");
        return builder.ToString();
    }

    /// <summary>
    /// Encrypt private key using master encryption key
    /// </summary>
    private string EncryptPrivateKey(string pem)
    {
        using var aes = Aes.Create();
        aes.Key = DeriveKeyFromPassword(_encryptionKey);
        aes.GenerateIV();
        var iv = aes.IV;
        using var ms = new MemoryStream();
        ms.Write(iv, 0, iv.Length);
        using var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write);
        var bytes = Encoding.UTF8.GetBytes(pem);
        cs.Write(bytes, 0, bytes.Length);
        cs.FlushFinalBlock();
        return Convert.ToBase64String(ms.ToArray());
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
