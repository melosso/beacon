using Beacon.Core.Models;
using Beacon.Core.Services;
using Cronos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Beacon.Storage;

public sealed class DataPolicyWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DataPolicyWorker> _logger;
    private readonly DataPolicyTrigger _trigger;

    public DataPolicyWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<DataPolicyWorker> logger,
        DataPolicyTrigger trigger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _trigger = trigger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogDebug("Data policy worker started");

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try { await ProcessBatchAsync(WorkflowTrigger.Cron, isManual: false, stoppingToken); }
                catch (Exception ex) { _logger.LogError(ex, "Data policy worker encountered an unhandled error"); }

                var cron = ReadCurrentCron();
                var next = cron.GetNextOccurrence(DateTimeOffset.UtcNow, TimeZoneInfo.Utc);
                if (next is null) break;

                var delay = next.Value - DateTimeOffset.UtcNow;
                if (delay > TimeSpan.Zero)
                {
                    _logger.LogDebug("Data policy worker: next run at {Next} (cron={Cron})", next.Value.UtcDateTime.ToString("o"), cron);
                    var wokenBySignal = await WaitForTriggerOrDelayAsync(delay, stoppingToken);
                    if (wokenBySignal)
                    {
                        var isManual = _trigger.ConsumeIsManual();
                        try { await ProcessBatchAsync(WorkflowTrigger.Manual, isManual: true, stoppingToken); }
                        catch (Exception ex) { _logger.LogError(ex, "Data policy worker encountered an unhandled error (manual trigger)"); }
                    }
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogDebug("Data policy worker stopping due to cancellation");
        }

        _logger.LogDebug("Data policy worker stopped");
    }

    /// <summary>Returns true if woken by a trigger signal, false if the cron delay elapsed.</summary>
    private async Task<bool> WaitForTriggerOrDelayAsync(TimeSpan maxDelay, CancellationToken stoppingToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        cts.CancelAfter(maxDelay);
        try
        {
            await _trigger.WaitAsync(cts.Token);
            _logger.LogDebug("Data policy worker: woken early by manual trigger, processing immediately");
            return true;
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            // Normal: cron interval elapsed, not an app shutdown.
            return false;
        }
    }

    private CronExpression ReadCurrentCron()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var config = scope.ServiceProvider.GetRequiredService<ISystemConfigurationService>().Get();
            return CronExpression.Parse(config.DataPolicyCron);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Data policy worker: invalid cron expression in config, falling back to daily at midnight");
            return CronExpression.Parse("0 0 * * *");
        }
    }

    internal async Task ProcessBatchAsync(WorkflowTrigger triggeredBy, bool isManual, CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;

        var config = sp.GetRequiredService<ISystemConfigurationService>().Get();

        // Cron runs only when data policies are explicitly enabled.
        // Manual runs always proceed so the operator can test individual policies.
        if (!isManual && !config.DataPoliciesEnabled)
        {
            _logger.LogDebug("Data policy worker: skipping batch, data policies are disabled");
            return;
        }

        var consent = sp.GetRequiredService<IConsentRepository>();
        var tasks = sp.GetRequiredService<WorkflowTaskRepository>();
        var now = DateTime.UtcNow;

        if (config.RetentionPurgeEnabled)
        {
            await RunPolicyAsync(
                tasks, consent, triggeredBy, WorkflowTaskType.RetentionPurge, now,
                (repo, ct) => repo.CountOptedOutToAnonymiseAsync(now.AddDays(-config.RetentionPurgeDays), ct),
                (repo, ct) => repo.AnonymiseOptedOutAsync(now.AddDays(-config.RetentionPurgeDays), ct),
                $"Anonymised opted-out records older than {config.RetentionPurgeDays} days",
                requireApproval: config.RetentionPurgeRequireApproval,
                stoppingToken);
        }

        if (config.PendingConfirmationPurgeEnabled && !stoppingToken.IsCancellationRequested)
        {
            await RunPolicyAsync(
                tasks, consent, triggeredBy, WorkflowTaskType.PendingConfirmationPurge, now,
                (repo, ct) => repo.CountPendingConfirmationToPurgeAsync(now.AddDays(-config.PendingConfirmationPurgeDays), ct),
                (repo, ct) => repo.PurgePendingConfirmationAsync(now.AddDays(-config.PendingConfirmationPurgeDays), ct),
                $"Purged pending confirmation records older than {config.PendingConfirmationPurgeDays} days",
                requireApproval: config.PendingConfirmationPurgeRequireApproval,
                stoppingToken);
        }

        if (!config.RetentionPurgeEnabled && !config.PendingConfirmationPurgeEnabled)
        {
            _logger.LogDebug("Data policy worker: no individual policies are enabled, nothing to run");
        }
    }

    private async Task RunPolicyAsync(
        WorkflowTaskRepository taskRepo,
        IConsentRepository consentRepo,
        WorkflowTrigger triggeredBy,
        WorkflowTaskType taskType,
        DateTime scheduledAt,
        Func<IConsentRepository, CancellationToken, Task<int>> countCheck,
        Func<IConsentRepository, CancellationToken, Task<int>> operation,
        string notes,
        bool requireApproval,
        CancellationToken stoppingToken)
    {
        var pending = await countCheck(consentRepo, stoppingToken);
        if (pending == 0)
        {
            _logger.LogDebug("Data policy: {TaskType} skipped. Nothing to process", taskType);
            return;
        }

        if (requireApproval)
        {
            // Queue for manual approval
            await taskRepo.CreateAsync(new WorkflowTask
            {
                Id = Guid.NewGuid(),
                TaskType = taskType,
                Status = WorkflowTaskStatus.PendingApproval,
                TriggeredBy = triggeredBy,
                ScheduledAt = scheduledAt
            }, stoppingToken);
            _logger.LogInformation(
                "Data policy: {TaskType} queued for approval (triggeredBy={TriggeredBy}, pending={Pending})",
                taskType, triggeredBy, pending);
            return;
        }

        var task = await taskRepo.CreateAsync(new WorkflowTask
        {
            Id = Guid.NewGuid(),
            TaskType = taskType,
            Status = WorkflowTaskStatus.Running,
            TriggeredBy = triggeredBy,
            ScheduledAt = scheduledAt,
            StartedAt = DateTime.UtcNow
        }, stoppingToken);

        try
        {
            var affected = await operation(consentRepo, stoppingToken);
            task = task with
            {
                Status = WorkflowTaskStatus.Completed,
                CompletedAt = DateTime.UtcNow,
                RecordsAffected = affected,
                Notes = notes
            };
            _logger.LogInformation(
                "Data policy: {TaskType} completed (triggeredBy={TriggeredBy}, affected={Affected})",
                taskType, triggeredBy, affected);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            task = task with
            {
                Status = WorkflowTaskStatus.Failed,
                CompletedAt = DateTime.UtcNow,
                ErrorMessage = ex.Message
            };
            _logger.LogError(ex, "Data policy: {TaskType} failed", taskType);
        }

        await taskRepo.UpdateAsync(task, stoppingToken);
    }
}
