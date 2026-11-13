namespace Beacon.Core.Models;

public enum WorkflowTaskStatus
{
    PendingApproval,
    Running,
    Completed,
    Failed,
    Rejected
}

public enum WorkflowTrigger
{
    Cron,
    Manual
}

public enum WorkflowTaskType
{
    RetentionPurge,
    PendingConfirmationPurge
}

public sealed record WorkflowTask
{
    public Guid Id { get; init; }
    public WorkflowTaskType TaskType { get; init; }
    public WorkflowTaskStatus Status { get; init; }
    public WorkflowTrigger TriggeredBy { get; init; } = WorkflowTrigger.Cron;
    public DateTime ScheduledAt { get; init; }
    public DateTime? StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public int RecordsAffected { get; init; }
    public string? Notes { get; init; }
    public string? ErrorMessage { get; init; }
}
