using Beacon.Core.Models;
using Beacon.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Storage;

public sealed class SubmissionFormRepository : ISubmissionFormRepository
{
    private readonly BeaconDbContext _context;

    public SubmissionFormRepository(BeaconDbContext context)
    {
        _context = context;
    }

    public async Task<SubmissionForm?> GetByIdAsync(Guid id)
    {
        return await _context.SubmissionForms.FindAsync(id);
    }

    public async Task<List<SubmissionForm>> GetAllAsync()
    {
        return await _context.SubmissionForms
            .OrderByDescending(f => f.CreatedAt)
            .ToListAsync();
    }

    public async Task CreateAsync(SubmissionForm form)
    {
        form.CreatedAt = DateTime.UtcNow;
        await _context.SubmissionForms.AddAsync(form);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateAsync(SubmissionForm form)
    {
        form.UpdatedAt = DateTime.UtcNow;
        _context.SubmissionForms.Update(form);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        var form = await _context.SubmissionForms.FindAsync(id);
        if (form != null)
        {
            _context.SubmissionForms.Remove(form);
            await _context.SaveChangesAsync();
        }
    }

    public async Task IncrementSubmissionCountAsync(Guid id)
    {
        var form = await _context.SubmissionForms.FindAsync(id);
        if (form != null)
        {
            form.SubmissionCount++;
            await _context.SaveChangesAsync();
        }
    }
}
