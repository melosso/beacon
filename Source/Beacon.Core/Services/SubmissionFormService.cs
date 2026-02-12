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

    public async Task SubscribeAsync(SubmissionForm form, string email, string? ipAddress = null, string? consentText = null, string? origin = null)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var normalizedBucket = form.Bucket.Trim().ToLowerInvariant();

        // Create consent for each permission (supports comma-separated)
        var permissions = form.Permission
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var resolvedCustomFields = ResolveCustomFieldVariables(form.CustomFields, form, ipAddress, origin);

        foreach (var permission in permissions)
        {
            await _consentService.OverrideAsync(
                normalizedBucket,
                normalizedEmail,
                permission,
                ConsentStatus.OptedIn,
                customFieldsJson: resolvedCustomFields);
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

    private static string? ResolveCustomFieldVariables(string? customFieldsJson, SubmissionForm form, string? ipAddress, string? origin)
    {
        if (string.IsNullOrEmpty(customFieldsJson))
            return null;

        try
        {
            var fields = JsonSerializer.Deserialize<Dictionary<string, string>>(customFieldsJson);
            if (fields is null || fields.Count == 0)
                return null;

            var now = DateTime.UtcNow;
            var resolved = false;
            foreach (var key in fields.Keys.ToList())
            {
                var value = fields[key];
                if (value is null) continue;
                var trimmed = value.Trim();

                var newValue = ResolveVariable(trimmed, form, ipAddress, origin, now);
                if (newValue != null)
                {
                    fields[key] = newValue;
                    resolved = true;
                }
            }

            return resolved ? JsonSerializer.Serialize(fields) : customFieldsJson;
        }
        catch
        {
            return customFieldsJson;
        }
    }

    private static string? ResolveVariable(string variable, SubmissionForm form, string? ipAddress, string? origin, DateTime now)
    {
        // Case-insensitive match on the variable name
        if (string.Equals(variable, "{{uuid}}", StringComparison.OrdinalIgnoreCase))
            return Guid.NewGuid().ToString();

        if (string.Equals(variable, "{{timestamp}}", StringComparison.OrdinalIgnoreCase))
            return now.ToString("o"); // ISO 8601 UTC

        // {{date}} — UTC date, {{date:timezone}} — date in specified IANA timezone
        if (variable.StartsWith("{{date", StringComparison.OrdinalIgnoreCase) && variable.EndsWith("}}"))
        {
            var inner = variable[2..^2]; // "date" or "date:Europe/Berlin"
            var colonIdx = inner.IndexOf(':');
            if (colonIdx < 0)
                return now.ToString("yyyy-MM-dd");

            var tz = inner[(colonIdx + 1)..].Trim();
            try
            {
                var tzi = TimeZoneInfo.FindSystemTimeZoneById(tz);
                var local = TimeZoneInfo.ConvertTimeFromUtc(now, tzi);
                return local.ToString("yyyy-MM-dd");
            }
            catch (TimeZoneNotFoundException)
            {
                return now.ToString("yyyy-MM-dd"); // Fallback to UTC
            }
        }

        if (string.Equals(variable, "{{source}}", StringComparison.OrdinalIgnoreCase))
            return origin ?? "";

        if (string.Equals(variable, "{{form_id}}", StringComparison.OrdinalIgnoreCase))
            return form.Id.ToString();

        if (string.Equals(variable, "{{form_name}}", StringComparison.OrdinalIgnoreCase))
            return form.Name;

        if (string.Equals(variable, "{{ip_hash}}", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(ipAddress))
                return "";
            var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(ipAddress));
            return Convert.ToHexStringLower(bytes);
        }

        return null; // Not a recognized variable
    }
}
