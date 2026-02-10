using System.Security.Cryptography;
using System.Text.Json;
using Beacon.Core.Models;
using Beacon.Core.Security;
using Serilog;

namespace Beacon.Core.Services;

public sealed class SubmissionFormService : ISubmissionFormService
{
    private static readonly ILogger Logger = Log.ForContext<SubmissionFormService>();

    private readonly ISubmissionFormRepository _repository;
    private readonly IConsentService _consentService;
    private readonly IConsentRepository _consentRepository;
    private readonly IWebhookService _webhookService;
    private readonly Encryptor _encryptor;
    private readonly EmailHasher _emailHasher;

    public SubmissionFormService(
        ISubmissionFormRepository repository,
        IConsentService consentService,
        IConsentRepository consentRepository,
        IWebhookService webhookService,
        Encryptor encryptor,
        EmailHasher emailHasher)
    {
        _repository = repository;
        _consentService = consentService;
        _consentRepository = consentRepository;
        _webhookService = webhookService;
        _encryptor = encryptor;
        _emailHasher = emailHasher;
    }

    public async Task<SubmissionForm?> GetFormAsync(Guid id)
    {
        return await _repository.GetByIdAsync(id);
    }

    public async Task<List<SubmissionForm>> GetAllFormsAsync()
    {
        return await _repository.GetAllAsync();
    }

    public async Task<(SubmissionForm Form, string PlaintextToken)> CreateFormAsync(SubmissionForm form)
    {
        // Generate a plaintext API token
        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        var plaintextToken = Convert.ToBase64String(tokenBytes);

        form.Id = Guid.NewGuid();
        form.EncryptedApiToken = _encryptor.Encrypt(plaintextToken);

        await _repository.CreateAsync(form);

        return (form, plaintextToken);
    }

    public async Task UpdateFormAsync(SubmissionForm form)
    {
        await _repository.UpdateAsync(form);
    }

    public async Task DeleteFormAsync(Guid id)
    {
        await _repository.DeleteAsync(id);
    }

    public bool ValidateOrigin(SubmissionForm form, string? origin)
    {
        if (string.IsNullOrWhiteSpace(origin))
            return false;

        try
        {
            var allowedOrigins = JsonSerializer.Deserialize<List<string>>(form.AllowedOrigins);
            if (allowedOrigins == null || allowedOrigins.Count == 0)
                return false;

            var normalizedOrigin = origin.TrimEnd('/').ToLowerInvariant();
            return allowedOrigins.Any(o => o.TrimEnd('/').ToLowerInvariant() == normalizedOrigin);
        }
        catch
        {
            return false;
        }
    }

    public async Task SubscribeAsync(SubmissionForm form, string email)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var normalizedBucket = form.Bucket.Trim().ToLowerInvariant();

        // Create consent for each permission (supports comma-separated)
        var permissions = form.Permission
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var permission in permissions)
        {
            await _consentService.EnsureAsync(
                normalizedBucket,
                normalizedEmail,
                permission,
                ConsentStatus.OptedIn);
        }

        // Increment submission count
        await _repository.IncrementSubmissionCountAsync(form.Id);

        // Trigger webhook if configured
        try
        {
            var emailHash = _emailHasher.Hash(normalizedEmail);
            var records = await _consentRepository.GetByEmailAsync(normalizedBucket, emailHash);
            var permissionStates = records.Select(r => new PermissionState
            {
                Permission = r.Permission,
                Status = r.Status
            }).ToList();

            if (permissionStates.Count > 0)
            {
                var data = new WebhookTriggerData
                {
                    Bucket = normalizedBucket,
                    Email = normalizedEmail,
                    EmailHash = emailHash,
                    Permissions = permissionStates
                };

                await _webhookService.TriggerWebhookAsync(normalizedBucket, data);
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to trigger webhook for submission in bucket {Bucket}", form.Bucket);
        }
    }
}
