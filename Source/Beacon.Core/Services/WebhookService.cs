using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Beacon.Core.Models;
using Beacon.Core.Security;
using Serilog;

namespace Beacon.Core.Services;

public sealed class WebhookService : IWebhookService
{
    private static readonly ILogger Logger = Log.ForContext<WebhookService>();

    private readonly IWebhookRepository _repository;
    private readonly Encryptor _encryptor;
    private readonly IWebhookDeliveryQueue _deliveryQueue;

    public WebhookService(
        IWebhookRepository repository,
        Encryptor encryptor,
        IWebhookDeliveryQueue deliveryQueue)
    {
        _repository = repository;
        _encryptor = encryptor;
        _deliveryQueue = deliveryQueue;
    }

    public async Task<WebhookConfig?> GetWebhookConfigAsync(string bucket)
    {
        var config = await _repository.GetByBucketAsync(bucket);
        if (config == null)
        {
            return null;
        }

        try
        {
            var decryptedConfig = new WebhookConfig
            {
                Id = config.Id,
                Bucket = config.Bucket,
                EncryptedUrl = _encryptor.Decrypt(config.EncryptedUrl),
                EncryptedMethod = _encryptor.Decrypt(config.EncryptedMethod),
                EncryptedHeaders = config.EncryptedHeaders != null
                    ? _encryptor.Decrypt(config.EncryptedHeaders)
                    : null,
                EncryptedSecret = null, // Never return the secret after initial creation
                BodyTemplate = config.BodyTemplate,
                IsEnabled = config.IsEnabled,
                CreatedAt = config.CreatedAt,
                LastTriggeredAt = config.LastTriggeredAt,
                TriggerCount = config.TriggerCount
            };
            return decryptedConfig;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to decrypt webhook config for bucket {Bucket}", bucket);
            return null;
        }
    }

    public async Task<string> SaveWebhookConfigAsync(
        string bucket,
        string url,
        string method,
        Dictionary<string, string>? headers,
        string? bodyTemplate)
    {
        var normalizedBucket = bucket.Trim().ToLowerInvariant();

        // Generate a signing secret for HMAC verification
        var secretBytes = RandomNumberGenerator.GetBytes(32);
        var secret = Convert.ToBase64String(secretBytes);

        var config = new WebhookConfig
        {
            Id = Guid.NewGuid(),
            Bucket = normalizedBucket,
            EncryptedUrl = _encryptor.Encrypt(url),
            EncryptedMethod = _encryptor.Encrypt(method.ToUpperInvariant()),
            EncryptedHeaders = headers != null && headers.Count > 0
                ? _encryptor.Encrypt(JsonSerializer.Serialize(headers))
                : null,
            EncryptedSecret = _encryptor.Encrypt(secret),
            BodyTemplate = bodyTemplate,
            IsEnabled = true
        };

        await _repository.UpsertAsync(config);

        // Return the plaintext secret so the user can configure their receiver
        return secret;
    }

    public async Task DeleteWebhookConfigAsync(string bucket)
    {
        var normalizedBucket = bucket.Trim().ToLowerInvariant();
        await _repository.DeleteByBucketAsync(normalizedBucket);
    }

    public async Task TriggerWebhookAsync(string bucket, WebhookTriggerData data)
    {
        var config = await _repository.GetByBucketAsync(bucket);
        if (config == null || !config.IsEnabled)
        {
            return;
        }

        try
        {
            var url = _encryptor.Decrypt(config.EncryptedUrl);
            var method = _encryptor.Decrypt(config.EncryptedMethod);

            Dictionary<string, string>? headers = null;
            if (!string.IsNullOrEmpty(config.EncryptedHeaders))
            {
                var headersJson = _encryptor.Decrypt(config.EncryptedHeaders);
                headers = JsonSerializer.Deserialize<Dictionary<string, string>>(headersJson);
            }

            var body = SubstituteVariables(config.BodyTemplate, data);

            // Compute HMAC-SHA256 signature
            string? signature = null;
            if (!string.IsNullOrEmpty(config.EncryptedSecret) && !string.IsNullOrEmpty(body))
            {
                var secret = _encryptor.Decrypt(config.EncryptedSecret);
                signature = ComputeSignature(body, secret);
            }

            var message = new WebhookDeliveryMessage
            {
                WebhookConfigId = config.Id,
                Url = url,
                Method = method,
                Headers = headers,
                Body = body,
                SignatureHeader = signature,
                Bucket = bucket
            };

            await _deliveryQueue.EnqueueAsync(message);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to enqueue webhook for bucket {Bucket}", bucket);
        }
    }

    public async Task<IReadOnlyList<string>> GetWebhookBucketsAsync()
    {
        var configs = await _repository.GetAllAsync();
        return configs
            .Where(c => c.IsEnabled)
            .Select(c => c.Bucket)
            .ToList();
    }

    private static string ComputeSignature(string payload, string secret)
    {
        var keyBytes = Convert.FromBase64String(secret);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var hash = HMACSHA256.HashData(keyBytes, payloadBytes);
        return $"sha256={Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    internal static string? SubstituteVariables(string? template, WebhookTriggerData data)
    {
        if (string.IsNullOrEmpty(template))
        {
            return template;
        }

        var result = template;
        result = Regex.Replace(result, @"\{\{bucket\}\}", JsonEscape(data.Bucket), RegexOptions.IgnoreCase);
        result = Regex.Replace(result, @"\{\{email\}\}", JsonEscape(data.Email), RegexOptions.IgnoreCase);
        result = Regex.Replace(result, @"\{\{emailHash\}\}", JsonEscape(data.EmailHash), RegexOptions.IgnoreCase);
        result = Regex.Replace(result, @"\{\{timestamp\}\}", JsonEscape(data.Timestamp.ToString("O")), RegexOptions.IgnoreCase);

        // {{permissions}} outputs the raw JSON array of all permission states
        var permissionsJson = JsonSerializer.Serialize(data.Permissions.Select(p => new
        {
            permission = p.Permission,
            status = p.Status.ToString()
        }));
        result = Regex.Replace(result, @"\{\{permissions\}\}", permissionsJson, RegexOptions.IgnoreCase);
        // Backwards compat alias
        result = Regex.Replace(result, @"\{\{changes\}\}", permissionsJson, RegexOptions.IgnoreCase);

        if (!string.IsNullOrEmpty(data.CustomFields))
        {
            result = Regex.Replace(result, @"\{\{customFields\}\}", JsonEscape(data.CustomFields), RegexOptions.IgnoreCase);
        }

        return result;
    }

    private static string JsonEscape(string value)
    {
        // Serialize produces "\"escaped\"", strip the surrounding quotes
        var serialized = JsonSerializer.Serialize(value);
        return serialized[1..^1];
    }
}
