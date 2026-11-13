using Beacon.Core.Models;
using Beacon.Core.Services;

namespace Beacon.Storage;

public enum TaskOperationOutcome { Success, NotFound, InvalidStatus }

public sealed record TaskOperationResult(TaskOperationOutcome Outcome, WorkflowTask? Task);

public sealed class DataPolicyService
{
    private readonly WorkflowTaskRepository _taskRepo;
    private readonly IConsentRepository _consentRepo;
    private readonly ISystemConfigurationService _configSvc;

    public DataPolicyService(
        WorkflowTaskRepository taskRepo,
        IConsentRepository consentRepo,
        ISystemConfigurationService configSvc)
    {
        _taskRepo = taskRepo;
        _consentRepo = consentRepo;
        _configSvc = configSvc;
    }

    public async Task<TaskOperationResult> ApproveTaskAsync(Guid id, CancellationToken ct = default)
    {
        var task = await _taskRepo.GetByIdAsync(id, ct);
        if (task is null)
            return new TaskOperationResult(TaskOperationOutcome.NotFound, null);

        var running = task with { Status = WorkflowTaskStatus.Running, StartedAt = DateTime.UtcNow };
        if (!await _taskRepo.TransitionAsync(WorkflowTaskStatus.PendingApproval, running, ct))
            return new TaskOperationResult(TaskOperationOutcome.InvalidStatus, task);

        return new TaskOperationResult(TaskOperationOutcome.Success, await RunAsync(running, ct));
    }

    public async Task<TaskOperationResult> RejectTaskAsync(Guid id, CancellationToken ct = default)
    {
        var task = await _taskRepo.GetByIdAsync(id, ct);
        if (task is null)
            return new TaskOperationResult(TaskOperationOutcome.NotFound, null);

        var rejected = task with { Status = WorkflowTaskStatus.Rejected, CompletedAt = DateTime.UtcNow };
        if (!await _taskRepo.TransitionAsync(WorkflowTaskStatus.PendingApproval, rejected, ct))
            return new TaskOperationResult(TaskOperationOutcome.InvalidStatus, task);

        return new TaskOperationResult(TaskOperationOutcome.Success, rejected);
    }

    private static int PolicyDays(SystemConfig config, WorkflowTaskType type) => type switch
    {
        WorkflowTaskType.RetentionPurge => config.RetentionPurgeDays,
        WorkflowTaskType.PendingConfirmationPurge => config.PendingConfirmationPurgeDays,
        _ => throw new InvalidOperationException($"Unknown task type: {type}")
    };

    /// <summary>Counts records the policy affects for a task scheduled at <paramref name="scheduledAt"/>.</summary>
    public Task<int> CountAsync(WorkflowTaskType type, DateTime scheduledAt, int policyDays, CancellationToken ct = default)
    {
        var cutoff = scheduledAt.AddDays(-policyDays);
        return type switch
        {
            WorkflowTaskType.RetentionPurge => _consentRepo.CountOptedOutToAnonymiseAsync(cutoff, ct),
            WorkflowTaskType.PendingConfirmationPurge => _consentRepo.CountPendingConfirmationToPurgeAsync(cutoff, ct),
            _ => throw new InvalidOperationException($"Unknown task type: {type}")
        };
    }

    /// <summary>Executes a Running task and stores the Completed or Failed result.</summary>
    internal async Task<WorkflowTask> RunAsync(WorkflowTask running, CancellationToken ct)
    {
        WorkflowTask done;
        try
        {
            // Cutoff derives from the task: an approved task affects the set counted at scheduling.
            var days = running.PolicyDays ?? PolicyDays(_configSvc.Get(), running.TaskType);
            var cutoff = running.ScheduledAt.AddDays(-days);
            var (affected, notes) = running.TaskType switch
            {
                WorkflowTaskType.RetentionPurge => (
                    await _consentRepo.AnonymiseOptedOutAsync(cutoff, ct),
                    $"Anonymised opted-out records older than {days} days"),
                WorkflowTaskType.PendingConfirmationPurge => (
                    await _consentRepo.PurgePendingConfirmationAsync(cutoff, ct),
                    $"Opted out and anonymised pending confirmation records older than {days} days"),
                _ => throw new InvalidOperationException($"Unknown task type: {running.TaskType}")
            };
            done = running with { Status = WorkflowTaskStatus.Completed, CompletedAt = DateTime.UtcNow, RecordsAffected = affected, Notes = notes };
        }
        catch (Exception ex)
        {
            var failed = running with { Status = WorkflowTaskStatus.Failed, CompletedAt = DateTime.UtcNow, ErrorMessage = ex.Message };
            // Records the failure after cancellation of the caller's token.
            await _taskRepo.TransitionAsync(WorkflowTaskStatus.Running, failed, CancellationToken.None);
            if (ex is OperationCanceledException) throw;
            return failed;
        }

        await _taskRepo.TransitionAsync(WorkflowTaskStatus.Running, done, CancellationToken.None);
        return done;
    }
}
