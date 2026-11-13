using Beacon.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Storage;

public sealed class WorkflowTaskRepository {
    private readonly BeaconDbContext _context;

    public WorkflowTaskRepository(BeaconDbContext context)
    {
        _context = context;
    }

    public async Task<WorkflowTask> CreateAsync(WorkflowTask task, CancellationToken ct = default)
    {
        _context.WorkflowTasks.Add(task);
        await _context.SaveChangesAsync(ct);
        return task;
    }

    /// <summary>Writes <paramref name="next"/> if the stored status equals <paramref name="from"/>. Returns false otherwise.</summary>
    public async Task<bool> TransitionAsync(WorkflowTaskStatus from, WorkflowTask next, CancellationToken ct = default)
    {
        return await _context.WorkflowTasks
            .Where(t => t.Id == next.Id && t.Status == from)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, next.Status)
                .SetProperty(t => t.StartedAt, next.StartedAt)
                .SetProperty(t => t.CompletedAt, next.CompletedAt)
                .SetProperty(t => t.RecordsAffected, next.RecordsAffected)
                .SetProperty(t => t.Notes, next.Notes)
                .SetProperty(t => t.ErrorMessage, next.ErrorMessage), ct) == 1;
    }

    public async Task<bool> HasPendingApprovalAsync(WorkflowTaskType type, CancellationToken ct = default)
    {
        return await _context.WorkflowTasks.AnyAsync(t => t.TaskType == type && t.Status == WorkflowTaskStatus.PendingApproval, ct);
    }

    public async Task<WorkflowTask?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.WorkflowTasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
    }

    public async Task<IReadOnlyList<WorkflowTask>> GetRecentAsync(int limit = 50, CancellationToken ct = default)
    {
        return await _context.WorkflowTasks
            .OrderByDescending(t => t.ScheduledAt)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<int> DeleteOldAsync(DateTime olderThan, CancellationToken ct = default)
    {
        return await _context.WorkflowTasks
            .Where(t => t.ScheduledAt < olderThan)
            .ExecuteDeleteAsync(ct);
    }
}
