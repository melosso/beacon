using System.Security.Cryptography;
using System.Text;
using Beacon.Core.Models;
using Beacon.Core.Security;
using Beacon.Core.Services;
using Beacon.Storage;

namespace Beacon.Api;

/// <summary>
/// Double opt-in decision and confirmation-email enqueueing, shared by every path that opts an email in.
/// </summary>
internal static class DoubleOptIn
{
    public static async Task<bool> IsActiveAsync(
        InstanceOptions instance, SystemConfig config, BucketOptionsRepository bucketOptions, string bucket, bool formRequiresIt = false)
    {
        var provider = config.EmailProvider?.Trim().ToLowerInvariant() ?? string.Empty;
        if (instance.DisableEmailNotifications || !config.EnableDoubleOptIn || !config.EmailNotifications || provider is "none" or "")
            return false;

        return formRequiresIt || (await bucketOptions.GetAsync(bucket)).DoubleOptIn;
    }

    public static string BaseUrl(InstanceOptions instance, HttpContext context) =>
        !string.IsNullOrEmpty(instance.PublicUrl)
            ? instance.PublicUrl.TrimEnd('/')
            : $"{context.Request.Scheme}://{context.Request.Host}";

    public static async Task EnqueueAsync(
        EmailQueueRepository queue, Encryptor encryptor, string baseUrl, bool perPermission,
        string bucket, string email, string emailHash, IReadOnlyList<string> permissions, string language)
    {
        if (permissions.Count == 0) return;

        var encryptedEmail = encryptor.Encrypt(email.Trim().ToLowerInvariant());

        if (perPermission)
        {
            foreach (var permission in permissions)
            {
                await queue.CancelPendingAsync(bucket, emailHash, permission);
                await queue.EnqueueAsync(NewEntry(permission));
            }
            return;
        }

        // Sorted so the key is stable regardless of input order.
        var allPermissions = string.Join(",", permissions.Order());

        // Cancel the combined key and each permission, to catch entries queued with a different set.
        foreach (var permission in permissions)
            await queue.CancelPendingAsync(bucket, emailHash, permission);
        await queue.CancelPendingAsync(bucket, emailHash, allPermissions);

        await queue.EnqueueAsync(NewEntry(allPermissions));

        EmailQueueEntry NewEntry(string permission)
        {
            var plain = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            return new EmailQueueEntry
            {
                Bucket = bucket,
                EncryptedEmail = encryptedEmail,
                EmailHash = emailHash,
                Permission = permission,
                Language = language,
                ConfirmationToken = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(plain))).ToLowerInvariant(),
                ConfirmationUrl = $"{baseUrl}/confirm/{plain}",
                ExpiresAt = DateTime.UtcNow.AddDays(7)
            };
        }
    }
}
