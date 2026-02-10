using Beacon.Core.Models;

namespace Beacon.Core.Services;

public interface ISubmissionFormService
{
    Task<SubmissionForm?> GetFormAsync(Guid id);
    Task<List<SubmissionForm>> GetAllFormsAsync();
    Task<(SubmissionForm Form, string PlaintextToken)> CreateFormAsync(SubmissionForm form);
    Task UpdateFormAsync(SubmissionForm form);
    Task DeleteFormAsync(Guid id);
    bool ValidateOrigin(SubmissionForm form, string? origin);
    Task SubscribeAsync(SubmissionForm form, string email, string? ipAddress = null, string? consentText = null);
}
