using Beacon.Core.Models;

namespace Beacon.Core.Services;

public interface ISubmissionFormRepository
{
    Task<SubmissionForm?> GetByIdAsync(Guid id);
    Task<List<SubmissionForm>> GetAllAsync();
    Task CreateAsync(SubmissionForm form);
    Task UpdateAsync(SubmissionForm form);
    Task DeleteAsync(Guid id);
    Task IncrementSubmissionCountAsync(Guid id);
}
