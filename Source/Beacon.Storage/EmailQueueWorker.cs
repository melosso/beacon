using Beacon.Core.Security;
using Beacon.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Beacon.Storage;

public sealed class EmailQueueWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EmailQueueWorker> _logger;
    private readonly bool _disabled;

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
    private const int MaxAttempts = 3;

    public EmailQueueWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<EmailQueueWorker> logger,
        InstanceOptions instanceOptions)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _disabled = instanceOptions.DisableEmailNotifications;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ProcessBatchAsync(); }
            catch (Exception ex) { _logger.LogError(ex, "Email queue worker encountered an unhandled error"); }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task ProcessBatchAsync()
    {
        if (_disabled) return;

        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;

        var config = sp.GetRequiredService<ISystemConfigurationService>().Get();

        if (!config.EmailNotifications || config.EmailProvider is "none" or "")
            return;

        var queue    = sp.GetRequiredService<IEmailQueueRepository>();
        var sender   = sp.GetRequiredService<IEmailSenderService>();
        var encryptor = sp.GetRequiredService<Encryptor>();

        await queue.PruneExpiredAsync();

        var entries = await queue.GetPendingBatchAsync(50);

        foreach (var entry in entries)
        {
            try
            {
                var email = encryptor.Decrypt(entry.EncryptedEmail);
                var sent  = await sender.SendConfirmationAsync(email, entry, config);

                if (sent)
                {
                    await queue.MarkSentAsync(entry.Id, DateTime.UtcNow);
                    _logger.LogInformation("Confirmation email sent (queue={Id}, bucket={Bucket})", entry.Id, entry.Bucket);
                }
                else
                {
                    await queue.MarkFailedAsync(entry.Id, "Sender returned false", NextRetry(entry.AttemptCount));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send confirmation email (queue={Id})", entry.Id);
                await queue.MarkFailedAsync(entry.Id, ex.Message, NextRetry(entry.AttemptCount));
            }
        }
    }

    private static DateTime? NextRetry(int attempts) =>
        attempts < MaxAttempts - 1
            ? DateTime.UtcNow.AddMinutes(Math.Pow(2, attempts + 1) * 5)
            : null;
}
