using Beacon.Core.Models;
using Beacon.Core.Services;
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

    public async Task UpdateAsync(WorkflowTask task, CancellationToken ct = default)
    {
        _context.WorkflowTasks.Update(task);
        await _context.SaveChangesAsync(ct);
    }

    public async Task<WorkflowTask?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.WorkflowTasks.FindAsync([id], ct);
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
