using Beacon.Core.Security;
using Beacon.Core.Services;
using Cronos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Beacon.Storage;

public sealed class EmailQueueWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EmailQueueWorker> _logger;
    private readonly EmailDispatchTrigger _trigger;
    private readonly bool _disabled;

    private const int MaxAttempts = 3;

    public EmailQueueWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<EmailQueueWorker> logger,
        EmailDispatchTrigger trigger,
        InstanceOptions instanceOptions)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _trigger = trigger;
        _disabled = instanceOptions.DisableEmailNotifications;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogDebug("Email queue worker started (notifications disabled at instance level: {Disabled})", _disabled);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try { await ProcessBatchAsync(stoppingToken); }
                catch (Exception ex) { _logger.LogError(ex, "Email queue worker encountered an unhandled error"); }

                var cron = ReadCurrentCron();
                var next = cron.GetNextOccurrence(DateTimeOffset.UtcNow, TimeZoneInfo.Utc);
                if (next is null) break;

                var delay = next.Value - DateTimeOffset.UtcNow;
                if (delay > TimeSpan.Zero)
                {
                    _logger.LogDebug("Email queue worker: next run at {Next} (cron={Cron})", next.Value.UtcDateTime.ToString("o"), cron);
                    await WaitForTriggerOrDelayAsync(delay, stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }

        _logger.LogDebug("Email queue worker stopped");
    }

    /// <summary>
    /// Waits until either the cron delay elapses or the API signals an early dispatch.
    /// </summary>
    private async Task WaitForTriggerOrDelayAsync(TimeSpan maxDelay, CancellationToken stoppingToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        cts.CancelAfter(maxDelay);
        try
        {
            await _trigger.WaitAsync(cts.Token);
            _logger.LogDebug("Email queue worker: woken early by API signal, processing immediately");
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            // Normal: cron interval elapsed, not an app shutdown.
        }
    }

    private CronExpression ReadCurrentCron()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var config = scope.ServiceProvider.GetRequiredService<ISystemConfigurationService>().Get();
            return CronExpression.Parse(config.EmailQueueCron);
        }
        catch
        {
            return CronExpression.Parse("*/5 * * * *");
        }
    }

    private async Task ProcessBatchAsync(CancellationToken stoppingToken)
    {
        if (_disabled)
        {
            _logger.LogDebug("Email queue worker: skipping batch, email notifications disabled at instance level");
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;

        var config = sp.GetRequiredService<ISystemConfigurationService>().Get();
        var queue   = sp.GetRequiredService<IEmailQueueRepository>();
        var now     = DateTime.UtcNow;

        // Purge runs on every cron tick, independent of email-sending configuration.
        await queue.PruneExpiredAsync();
        var retentionDays = config.EmailQueueRetentionDays > 0 ? config.EmailQueueRetentionDays : 90;
        var deleted = await queue.DeleteOldAsync(now.AddDays(-retentionDays));
        if (deleted > 0)
            _logger.LogInformation(
                "Email queue: purged {Count} completed records (retention={Days}d)",
                deleted, retentionDays);

        var providerLower = config.EmailProvider?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!config.EmailNotifications || providerLower is "none" or "")
        {
            _logger.LogDebug(
                "Email queue worker: skipping send, email not configured (enabled={EmailNotifications}, provider={EmailProvider})",
                config.EmailNotifications, config.EmailProvider);
            return;
        }

        var sender    = sp.GetRequiredService<IEmailSenderService>();
        var encryptor = sp.GetRequiredService<Encryptor>();

        var entries = await queue.GetPendingBatchAsync(50);
        if (entries.Count == 0)
        {
            _logger.LogDebug("Email queue worker: no pending entries");
            return;
        }

        _logger.LogInformation("Email queue worker: processing batch of {Count} pending entries", entries.Count);

        foreach (var entry in entries)
        {
            if (stoppingToken.IsCancellationRequested) break;

            try
            {
                var email = encryptor.Decrypt(entry.EncryptedEmail);
                var sent  = await sender.SendConfirmationAsync(email, entry, config, stoppingToken);

                if (sent)
                {
                    await queue.MarkSentAsync(entry.Id, DateTime.UtcNow);
                    _logger.LogInformation("Email queue: confirmation email sent (queue={Id}, bucket={Bucket}, permission={Permission})", entry.Id, entry.Bucket, entry.Permission);
                }
                else
                {
                    _logger.LogWarning("Email queue: provider declined to send, will retry (queue={Id}, bucket={Bucket}, provider={Provider})", entry.Id, entry.Bucket, config.EmailProvider);
                    await queue.MarkFailedAsync(entry.Id, "Sender returned false", NextRetry(entry.AttemptCount));
                }

                // Rate limiting: Resend allows 2 requests per second.
                // We add a 600ms delay between requests to stay safe.
                if (providerLower == "resend")
                {
                    await Task.Delay(600, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Email queue: failed to send confirmation email (queue={Id}, bucket={Bucket})", entry.Id, entry.Bucket);
                await queue.MarkFailedAsync(entry.Id, ex.Message, NextRetry(entry.AttemptCount));
            }
        }

        _logger.LogDebug("Email queue worker: batch complete");
    }

    private static DateTime? NextRetry(int attempts) =>
        attempts < MaxAttempts - 1
            ? DateTime.UtcNow.AddMinutes(Math.Pow(2, attempts + 1) * 5)
            : null;
}
