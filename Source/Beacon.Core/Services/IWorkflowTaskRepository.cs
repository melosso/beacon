using Beacon.Core.Models;

namespace Beacon.Core.Services;

public interface IWorkflowTaskRepository
{
    Task<WorkflowTask> CreateAsync(WorkflowTask task, CancellationToken ct = default);
    Task UpdateAsync(WorkflowTask task, CancellationToken ct = default);
    Task<WorkflowTask?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<WorkflowTask>> GetRecentAsync(int limit = 50, CancellationToken ct = default);
    Task<int> DeleteOldAsync(DateTime olderThan, CancellationToken ct = default);
}
