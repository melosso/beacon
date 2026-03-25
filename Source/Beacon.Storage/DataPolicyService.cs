using Beacon.Core.Models;
using Beacon.Core.Services;

namespace Beacon.Storage;

public enum TaskOperationOutcome { Success, NotFound, InvalidStatus }

public sealed record TaskOperationResult(TaskOperationOutcome Outcome, WorkflowTask? Task);

public sealed class DataPolicyService
{
    private readonly IWorkflowTaskRepository _taskRepo;
    private readonly IConsentRepository _consentRepo;
    private readonly ISystemConfigurationService _configSvc;

    public DataPolicyService(
        IWorkflowTaskRepository taskRepo,
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
        if (task.Status != WorkflowTaskStatus.PendingApproval)
            return new TaskOperationResult(TaskOperationOutcome.InvalidStatus, task);

        var config = _configSvc.Get();
        var now = DateTime.UtcNow;

        task = task with { Status = WorkflowTaskStatus.Running, StartedAt = now };
        await _taskRepo.UpdateAsync(task, ct);

        try
        {
            var (affected, notes) = task.TaskType switch
            {
                WorkflowTaskType.RetentionPurge => (
                    await _consentRepo.AnonymiseOptedOutAsync(now.AddDays(-config.RetentionPurgeDays), ct),
                    $"Anonymised opted-out records older than {config.RetentionPurgeDays} days"),
                WorkflowTaskType.IpAnonymization => (
                    await _consentRepo.AnonymiseIpAddressesAsync(now.AddDays(-config.IpAnonymizationDays), ct),
                    $"Anonymised IP addresses older than {config.IpAnonymizationDays} days"),
                WorkflowTaskType.PendingConfirmationPurge => (
                    await _consentRepo.PurgePendingConfirmationAsync(now.AddDays(-config.PendingConfirmationPurgeDays), ct),
                    $"Purged pending confirmation records older than {config.PendingConfirmationPurgeDays} days"),
                _ => throw new InvalidOperationException($"Unknown task type: {task.TaskType}")
            };
            task = task with
            {
                Status = WorkflowTaskStatus.Completed,
                CompletedAt = DateTime.UtcNow,
                RecordsAffected = affected,
                Notes = notes
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            task = task with
            {
                Status = WorkflowTaskStatus.Failed,
                CompletedAt = DateTime.UtcNow,
                ErrorMessage = ex.Message
            };
        }

        await _taskRepo.UpdateAsync(task, ct);
        return new TaskOperationResult(TaskOperationOutcome.Success, task);
    }

    public async Task<TaskOperationResult> RejectTaskAsync(Guid id, CancellationToken ct = default)
    {
        var task = await _taskRepo.GetByIdAsync(id, ct);
        if (task is null)
            return new TaskOperationResult(TaskOperationOutcome.NotFound, null);
        if (task.Status != WorkflowTaskStatus.PendingApproval)
            return new TaskOperationResult(TaskOperationOutcome.InvalidStatus, task);

        task = task with { Status = WorkflowTaskStatus.Rejected, CompletedAt = DateTime.UtcNow };
        await _taskRepo.UpdateAsync(task, ct);
        return new TaskOperationResult(TaskOperationOutcome.Success, task);
    }
}
