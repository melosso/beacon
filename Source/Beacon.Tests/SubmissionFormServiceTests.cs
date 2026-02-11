using System.Text.Json;
using Beacon.Core.Models;
using Beacon.Core.Security;
using Beacon.Core.Services;
using Xunit;

namespace Beacon.Tests;

public class SubmissionFormServiceTests
{
    private const string TestPepper = "test-pepper-for-hashing";
    private const string TestEncryptionKey = "MDEyMzQ1Njc4OTAxMjM0NTY3ODkwMTIzNDU2Nzg5MDE=";  // 32 bytes

    private SubmissionFormService CreateService(
        InMemorySubmissionFormRepository? formRepo = null,
        InMemoryConsentRepository? consentRepo = null,
        InMemoryWebhookService? webhookService = null)
    {
        formRepo ??= new InMemorySubmissionFormRepository();
        consentRepo ??= new InMemoryConsentRepository();
        webhookService ??= new InMemoryWebhookService();
        var emailHasher = new EmailHasher(TestPepper);
        var encryptor = new Encryptor(TestEncryptionKey);
        var consentService = new ConsentService(consentRepo, emailHasher, encryptor);

        return new SubmissionFormService(formRepo, consentService, consentRepo, webhookService, encryptor, emailHasher);
    }

    // --- CreateFormAsync ---

    [Fact]
    public async Task CreateFormAsync_AssignsIdAndEncryptsToken()
    {
        var formRepo = new InMemorySubmissionFormRepository();
        var service = CreateService(formRepo);

        var form = CreateTestForm();
        var (createdForm, plaintextToken) = await service.CreateFormAsync(form);

        Assert.NotEqual(Guid.Empty, createdForm.Id);
        Assert.False(string.IsNullOrEmpty(plaintextToken));
        Assert.NotEqual(plaintextToken, createdForm.EncryptedApiToken);
        Assert.Single(formRepo.Forms);
    }

    [Fact]
    public async Task CreateFormAsync_GeneratesUniqueTokensPerForm()
    {
        var service = CreateService();

        var (_, token1) = await service.CreateFormAsync(CreateTestForm());
        var (_, token2) = await service.CreateFormAsync(CreateTestForm());

        Assert.NotEqual(token1, token2);
    }

    [Fact]
    public async Task CreateFormAsync_GeneratesUniqueIds()
    {
        var service = CreateService();

        var (form1, _) = await service.CreateFormAsync(CreateTestForm());
        var (form2, _) = await service.CreateFormAsync(CreateTestForm());

        Assert.NotEqual(form1.Id, form2.Id);
    }

    // --- GetFormAsync ---

    [Fact]
    public async Task GetFormAsync_ReturnsCreatedForm()
    {
        var formRepo = new InMemorySubmissionFormRepository();
        var service = CreateService(formRepo);

        var (created, _) = await service.CreateFormAsync(CreateTestForm());
        var fetched = await service.GetFormAsync(created.Id);

        Assert.NotNull(fetched);
        Assert.Equal(created.Id, fetched.Id);
        Assert.Equal(created.Name, fetched.Name);
    }

    [Fact]
    public async Task GetFormAsync_ReturnsNullForNonExistent()
    {
        var service = CreateService();
        var result = await service.GetFormAsync(Guid.NewGuid());
        Assert.Null(result);
    }

    // --- GetAllFormsAsync ---

    [Fact]
    public async Task GetAllFormsAsync_ReturnsAllForms()
    {
        var service = CreateService();

        await service.CreateFormAsync(CreateTestForm("Form A"));
        await service.CreateFormAsync(CreateTestForm("Form B"));

        var all = await service.GetAllFormsAsync();
        Assert.Equal(2, all.Count);
    }

    // --- UpdateFormAsync ---

    [Fact]
    public async Task UpdateFormAsync_PersistsChanges()
    {
        var formRepo = new InMemorySubmissionFormRepository();
        var service = CreateService(formRepo);

        var (created, _) = await service.CreateFormAsync(CreateTestForm());
        created.Name = "Updated Name";
        await service.UpdateFormAsync(created);

        var fetched = await service.GetFormAsync(created.Id);
        Assert.NotNull(fetched);
        Assert.Equal("Updated Name", fetched.Name);
    }

    // --- DeleteFormAsync ---

    [Fact]
    public async Task DeleteFormAsync_RemovesForm()
    {
        var formRepo = new InMemorySubmissionFormRepository();
        var service = CreateService(formRepo);

        var (created, _) = await service.CreateFormAsync(CreateTestForm());
        await service.DeleteFormAsync(created.Id);

        var fetched = await service.GetFormAsync(created.Id);
        Assert.Null(fetched);
        Assert.Empty(formRepo.Forms);
    }

    // --- ValidateOrigin ---

    [Fact]
    public void ValidateOrigin_MatchingOrigin_ReturnsTrue()
    {
        var service = CreateService();
        var form = CreateTestForm();
        form.AllowedOrigins = JsonSerializer.Serialize(new[] { "https://example.com" });

        Assert.True(service.ValidateOrigin(form, "https://example.com"));
    }

    [Fact]
    public void ValidateOrigin_MatchingOriginWithTrailingSlash_ReturnsTrue()
    {
        var service = CreateService();
        var form = CreateTestForm();
        form.AllowedOrigins = JsonSerializer.Serialize(new[] { "https://example.com" });

        Assert.True(service.ValidateOrigin(form, "https://example.com/"));
    }

    [Fact]
    public void ValidateOrigin_CaseInsensitive_ReturnsTrue()
    {
        var service = CreateService();
        var form = CreateTestForm();
        form.AllowedOrigins = JsonSerializer.Serialize(new[] { "https://Example.COM" });

        Assert.True(service.ValidateOrigin(form, "https://example.com"));
    }

    [Fact]
    public void ValidateOrigin_NonMatchingOrigin_ReturnsFalse()
    {
        var service = CreateService();
        var form = CreateTestForm();
        form.AllowedOrigins = JsonSerializer.Serialize(new[] { "https://example.com" });

        Assert.False(service.ValidateOrigin(form, "https://evil.com"));
    }

    [Fact]
    public void ValidateOrigin_NullOrigin_ReturnsFalse()
    {
        var service = CreateService();
        var form = CreateTestForm();
        form.AllowedOrigins = JsonSerializer.Serialize(new[] { "https://example.com" });

        Assert.False(service.ValidateOrigin(form, null));
    }

    [Fact]
    public void ValidateOrigin_EmptyOrigin_ReturnsFalse()
    {
        var service = CreateService();
        var form = CreateTestForm();
        form.AllowedOrigins = JsonSerializer.Serialize(new[] { "https://example.com" });

        Assert.False(service.ValidateOrigin(form, ""));
    }

    [Fact]
    public void ValidateOrigin_EmptyAllowedOrigins_ReturnsFalse()
    {
        var service = CreateService();
        var form = CreateTestForm();
        form.AllowedOrigins = "[]";

        Assert.False(service.ValidateOrigin(form, "https://example.com"));
    }

    [Fact]
    public void ValidateOrigin_InvalidJson_ReturnsFalse()
    {
        var service = CreateService();
        var form = CreateTestForm();
        form.AllowedOrigins = "not-json";

        Assert.False(service.ValidateOrigin(form, "https://example.com"));
    }

    [Fact]
    public void ValidateOrigin_MultipleOrigins_MatchesAny()
    {
        var service = CreateService();
        var form = CreateTestForm();
        form.AllowedOrigins = JsonSerializer.Serialize(new[] { "https://a.com", "https://b.com", "https://c.com" });

        Assert.True(service.ValidateOrigin(form, "https://b.com"));
    }

    // --- SubscribeAsync ---

    [Fact]
    public async Task SubscribeAsync_CreatesConsentRecord()
    {
        var consentRepo = new InMemoryConsentRepository();
        var emailHasher = new EmailHasher(TestPepper);
        var encryptor = new Encryptor(TestEncryptionKey);
        var consentService = new ConsentService(consentRepo, emailHasher, encryptor);
        var formRepo = new InMemorySubmissionFormRepository();
        var webhookService = new InMemoryWebhookService();
        var service = new SubmissionFormService(formRepo, consentService, consentRepo, webhookService, encryptor, emailHasher);

        var form = CreateTestForm();
        form.Id = Guid.NewGuid();
        form.Bucket = "test-bucket";
        form.Permission = "newsletter";
        await formRepo.CreateAsync(form);

        await service.SubscribeAsync(form, "user@example.com");

        var status = await consentService.CheckAsync("test-bucket", "user@example.com", "newsletter");
        Assert.Equal(ConsentStatus.OptedIn, status);
    }

    [Fact]
    public async Task SubscribeAsync_IncrementsSubmissionCount()
    {
        var formRepo = new InMemorySubmissionFormRepository();
        var service = CreateService(formRepo);

        var form = CreateTestForm();
        form.Id = Guid.NewGuid();
        form.Permission = "newsletter";
        await formRepo.CreateAsync(form);

        await service.SubscribeAsync(form, "user@example.com");
        await service.SubscribeAsync(form, "other@example.com");

        var fetched = await formRepo.GetByIdAsync(form.Id);
        Assert.NotNull(fetched);
        Assert.Equal(2, fetched.SubmissionCount);
    }

    [Fact]
    public async Task SubscribeAsync_HandlesMultipleCommaSeparatedPermissions()
    {
        var consentRepo = new InMemoryConsentRepository();
        var emailHasher = new EmailHasher(TestPepper);
        var encryptor = new Encryptor(TestEncryptionKey);
        var consentService = new ConsentService(consentRepo, emailHasher, encryptor);
        var formRepo = new InMemorySubmissionFormRepository();
        var webhookService = new InMemoryWebhookService();
        var service = new SubmissionFormService(formRepo, consentService, consentRepo, webhookService, encryptor, emailHasher);

        var form = CreateTestForm();
        form.Id = Guid.NewGuid();
        form.Permission = "newsletter, alerts, updates";
        await formRepo.CreateAsync(form);

        await service.SubscribeAsync(form, "user@example.com");

        Assert.Equal(ConsentStatus.OptedIn, await consentService.CheckAsync(form.Bucket, "user@example.com", "newsletter"));
        Assert.Equal(ConsentStatus.OptedIn, await consentService.CheckAsync(form.Bucket, "user@example.com", "alerts"));
        Assert.Equal(ConsentStatus.OptedIn, await consentService.CheckAsync(form.Bucket, "user@example.com", "updates"));
    }

    [Fact]
    public async Task SubscribeAsync_NormalizesEmail()
    {
        var consentRepo = new InMemoryConsentRepository();
        var emailHasher = new EmailHasher(TestPepper);
        var encryptor = new Encryptor(TestEncryptionKey);
        var consentService = new ConsentService(consentRepo, emailHasher, encryptor);
        var formRepo = new InMemorySubmissionFormRepository();
        var webhookService = new InMemoryWebhookService();
        var service = new SubmissionFormService(formRepo, consentService, consentRepo, webhookService, encryptor, emailHasher);

        var form = CreateTestForm();
        form.Id = Guid.NewGuid();
        form.Permission = "newsletter";
        await formRepo.CreateAsync(form);

        await service.SubscribeAsync(form, "  USER@Example.COM  ");

        var status = await consentService.CheckAsync(form.Bucket, "user@example.com", "newsletter");
        Assert.Equal(ConsentStatus.OptedIn, status);
    }

    [Fact]
    public async Task SubscribeAsync_TriggersWebhook()
    {
        var webhookService = new InMemoryWebhookService();
        var formRepo = new InMemorySubmissionFormRepository();
        var service = CreateService(formRepo, webhookService: webhookService);

        var form = CreateTestForm();
        form.Id = Guid.NewGuid();
        form.Permission = "newsletter";
        await formRepo.CreateAsync(form);

        await service.SubscribeAsync(form, "user@example.com");

        Assert.Single(webhookService.TriggeredWebhooks);
        Assert.Equal(form.Bucket.ToLowerInvariant(), webhookService.TriggeredWebhooks[0].Bucket);
    }

    [Fact]
    public async Task SubscribeAsync_DoesNotOverwriteExistingOptOut()
    {
        var consentRepo = new InMemoryConsentRepository();
        var emailHasher = new EmailHasher(TestPepper);
        var encryptor = new Encryptor(TestEncryptionKey);
        var consentService = new ConsentService(consentRepo, emailHasher, encryptor);
        var formRepo = new InMemorySubmissionFormRepository();
        var webhookService = new InMemoryWebhookService();
        var service = new SubmissionFormService(formRepo, consentService, consentRepo, webhookService, encryptor, emailHasher);

        var form = CreateTestForm();
        form.Id = Guid.NewGuid();
        form.Bucket = "test-bucket";
        form.Permission = "newsletter";
        await formRepo.CreateAsync(form);

        // First, opt out
        await consentService.ProcessOptOutAsync("test-bucket", "user@example.com", ["newsletter"], "token123", ConsentSource.Url);

        // Then try to subscribe again via form
        await service.SubscribeAsync(form, "user@example.com");

        // EnsureAsync should not overwrite the opt-out
        var status = await consentService.CheckAsync("test-bucket", "user@example.com", "newsletter");
        Assert.Equal(ConsentStatus.OptedOut, status);
    }

    [Fact]
    public async Task SubscribeAsync_ResolvesUuidCustomField()
    {
        var consentRepo = new InMemoryConsentRepository();
        var emailHasher = new EmailHasher(TestPepper);
        var encryptor = new Encryptor(TestEncryptionKey);
        var consentService = new ConsentService(consentRepo, emailHasher, encryptor);
        var formRepo = new InMemorySubmissionFormRepository();
        var webhookService = new InMemoryWebhookService();
        var service = new SubmissionFormService(formRepo, consentService, consentRepo, webhookService, encryptor, emailHasher);

        var form = CreateTestForm();
        form.Id = Guid.NewGuid();
        form.Permission = "newsletter";
        form.CustomFields = JsonSerializer.Serialize(new Dictionary<string, string> { { "subscriber_id", "{{uuid}}" } });
        await formRepo.CreateAsync(form);

        await service.SubscribeAsync(form, "user@example.com");

        // The consent record should have a resolved UUID, not the placeholder
        var emailHash = emailHasher.Hash("user@example.com");
        var records = await consentRepo.GetByEmailAsync(form.Bucket.ToLowerInvariant(), emailHash);
        Assert.Single(records);
        Assert.NotNull(records[0].CustomFields);
        Assert.DoesNotContain("{{uuid}}", records[0].CustomFields);

        // Should be valid JSON with a GUID value
        var fields = JsonSerializer.Deserialize<Dictionary<string, string>>(records[0].CustomFields!);
        Assert.NotNull(fields);
        Assert.True(fields!.ContainsKey("subscriber_id"));
        Assert.True(Guid.TryParse(fields["subscriber_id"], out _));
    }

    [Fact]
    public async Task SubscribeAsync_ResolvesTimestampCustomField()
    {
        var (service, formRepo, consentRepo, emailHasher) = CreateFullService();
        var form = CreateTestForm();
        form.Id = Guid.NewGuid();
        form.Permission = "newsletter";
        form.CustomFields = JsonSerializer.Serialize(new Dictionary<string, string> { { "signed_at", "{{timestamp}}" } });
        await formRepo.CreateAsync(form);

        await service.SubscribeAsync(form, "user@example.com");

        var emailHash = emailHasher.Hash("user@example.com");
        var records = await consentRepo.GetByEmailAsync(form.Bucket.ToLowerInvariant(), emailHash);
        var fields = JsonSerializer.Deserialize<Dictionary<string, string>>(records[0].CustomFields!);
        Assert.NotNull(fields);
        Assert.True(fields!.ContainsKey("signed_at"));
        Assert.True(DateTime.TryParse(fields["signed_at"], out _));
    }

    [Fact]
    public async Task SubscribeAsync_ResolvesDateCustomField()
    {
        var (service, formRepo, consentRepo, emailHasher) = CreateFullService();
        var form = CreateTestForm();
        form.Id = Guid.NewGuid();
        form.Permission = "newsletter";
        form.CustomFields = JsonSerializer.Serialize(new Dictionary<string, string> { { "signup_date", "{{date}}" } });
        await formRepo.CreateAsync(form);

        await service.SubscribeAsync(form, "user@example.com");

        var emailHash = emailHasher.Hash("user@example.com");
        var records = await consentRepo.GetByEmailAsync(form.Bucket.ToLowerInvariant(), emailHash);
        var fields = JsonSerializer.Deserialize<Dictionary<string, string>>(records[0].CustomFields!);
        Assert.NotNull(fields);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", fields!["signup_date"]);
    }

    [Fact]
    public async Task SubscribeAsync_ResolvesDateWithTimezoneCustomField()
    {
        var (service, formRepo, consentRepo, emailHasher) = CreateFullService();
        var form = CreateTestForm();
        form.Id = Guid.NewGuid();
        form.Permission = "newsletter";
        form.CustomFields = JsonSerializer.Serialize(new Dictionary<string, string> { { "signup_date", "{{date:Europe/Berlin}}" } });
        await formRepo.CreateAsync(form);

        await service.SubscribeAsync(form, "user@example.com");

        var emailHash = emailHasher.Hash("user@example.com");
        var records = await consentRepo.GetByEmailAsync(form.Bucket.ToLowerInvariant(), emailHash);
        var fields = JsonSerializer.Deserialize<Dictionary<string, string>>(records[0].CustomFields!);
        Assert.NotNull(fields);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", fields!["signup_date"]);
    }

    [Fact]
    public async Task SubscribeAsync_ResolvesDateWithInvalidTimezoneFallsBackToUtc()
    {
        var (service, formRepo, consentRepo, emailHasher) = CreateFullService();
        var form = CreateTestForm();
        form.Id = Guid.NewGuid();
        form.Permission = "newsletter";
        form.CustomFields = JsonSerializer.Serialize(new Dictionary<string, string> { { "signup_date", "{{date:Invalid/Zone}}" } });
        await formRepo.CreateAsync(form);

        await service.SubscribeAsync(form, "user@example.com");

        var emailHash = emailHasher.Hash("user@example.com");
        var records = await consentRepo.GetByEmailAsync(form.Bucket.ToLowerInvariant(), emailHash);
        var fields = JsonSerializer.Deserialize<Dictionary<string, string>>(records[0].CustomFields!);
        Assert.NotNull(fields);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", fields!["signup_date"]);
    }

    [Fact]
    public async Task SubscribeAsync_ResolvesSourceCustomField()
    {
        var (service, formRepo, consentRepo, emailHasher) = CreateFullService();
        var form = CreateTestForm();
        form.Id = Guid.NewGuid();
        form.Permission = "newsletter";
        form.CustomFields = JsonSerializer.Serialize(new Dictionary<string, string> { { "referrer", "{{source}}" } });
        await formRepo.CreateAsync(form);

        await service.SubscribeAsync(form, "user@example.com", origin: "https://blog.example.com");

        var emailHash = emailHasher.Hash("user@example.com");
        var records = await consentRepo.GetByEmailAsync(form.Bucket.ToLowerInvariant(), emailHash);
        var fields = JsonSerializer.Deserialize<Dictionary<string, string>>(records[0].CustomFields!);
        Assert.NotNull(fields);
        Assert.Equal("https://blog.example.com", fields!["referrer"]);
    }

    [Fact]
    public async Task SubscribeAsync_ResolvesFormIdCustomField()
    {
        var (service, formRepo, consentRepo, emailHasher) = CreateFullService();
        var form = CreateTestForm();
        form.Id = Guid.NewGuid();
        form.Permission = "newsletter";
        form.CustomFields = JsonSerializer.Serialize(new Dictionary<string, string> { { "fid", "{{form_id}}" } });
        await formRepo.CreateAsync(form);

        await service.SubscribeAsync(form, "user@example.com");

        var emailHash = emailHasher.Hash("user@example.com");
        var records = await consentRepo.GetByEmailAsync(form.Bucket.ToLowerInvariant(), emailHash);
        var fields = JsonSerializer.Deserialize<Dictionary<string, string>>(records[0].CustomFields!);
        Assert.NotNull(fields);
        Assert.Equal(form.Id.ToString(), fields!["fid"]);
    }

    [Fact]
    public async Task SubscribeAsync_ResolvesFormNameCustomField()
    {
        var (service, formRepo, consentRepo, emailHasher) = CreateFullService();
        var form = CreateTestForm("My Landing Page");
        form.Id = Guid.NewGuid();
        form.Permission = "newsletter";
        form.CustomFields = JsonSerializer.Serialize(new Dictionary<string, string> { { "form", "{{form_name}}" } });
        await formRepo.CreateAsync(form);

        await service.SubscribeAsync(form, "user@example.com");

        var emailHash = emailHasher.Hash("user@example.com");
        var records = await consentRepo.GetByEmailAsync(form.Bucket.ToLowerInvariant(), emailHash);
        var fields = JsonSerializer.Deserialize<Dictionary<string, string>>(records[0].CustomFields!);
        Assert.NotNull(fields);
        Assert.Equal("My Landing Page", fields!["form"]);
    }

    [Fact]
    public async Task SubscribeAsync_ResolvesIpHashCustomField()
    {
        var (service, formRepo, consentRepo, emailHasher) = CreateFullService();
        var form = CreateTestForm();
        form.Id = Guid.NewGuid();
        form.Permission = "newsletter";
        form.CustomFields = JsonSerializer.Serialize(new Dictionary<string, string> { { "client", "{{ip_hash}}" } });
        await formRepo.CreateAsync(form);

        await service.SubscribeAsync(form, "user@example.com", ipAddress: "192.168.1.1");

        var emailHash = emailHasher.Hash("user@example.com");
        var records = await consentRepo.GetByEmailAsync(form.Bucket.ToLowerInvariant(), emailHash);
        var fields = JsonSerializer.Deserialize<Dictionary<string, string>>(records[0].CustomFields!);
        Assert.NotNull(fields);
        Assert.Equal(64, fields!["client"].Length); // SHA256 hex = 64 chars
        Assert.Matches(@"^[0-9a-f]{64}$", fields["client"]);
    }

    [Fact]
    public async Task SubscribeAsync_ResolvesMultipleVariables()
    {
        var (service, formRepo, consentRepo, emailHasher) = CreateFullService();
        var form = CreateTestForm();
        form.Id = Guid.NewGuid();
        form.Permission = "newsletter";
        form.CustomFields = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            { "id", "{{uuid}}" },
            { "when", "{{timestamp}}" },
            { "from", "{{source}}" },
            { "static_val", "keep-this" }
        });
        await formRepo.CreateAsync(form);

        await service.SubscribeAsync(form, "user@example.com", origin: "https://example.com");

        var emailHash = emailHasher.Hash("user@example.com");
        var records = await consentRepo.GetByEmailAsync(form.Bucket.ToLowerInvariant(), emailHash);
        var fields = JsonSerializer.Deserialize<Dictionary<string, string>>(records[0].CustomFields!);
        Assert.NotNull(fields);
        Assert.True(Guid.TryParse(fields!["id"], out _));
        Assert.True(DateTime.TryParse(fields["when"], out _));
        Assert.Equal("https://example.com", fields["from"]);
        Assert.Equal("keep-this", fields["static_val"]);
    }

    // --- IsRedirectAllowedForForm (tested via static helper in SubmissionEndpoints) ---

    [Fact]
    public void IsRedirectAllowedForForm_MatchingOrigin_ReturnsTrue()
    {
        var form = CreateTestForm();
        form.AllowedOrigins = JsonSerializer.Serialize(new[] { "https://example.com" });

        Assert.True(Beacon.Api.SubmissionEndpoints.IsRedirectAllowedForForm(form, "https://example.com/thank-you"));
    }

    [Fact]
    public void IsRedirectAllowedForForm_NonMatchingOrigin_ReturnsFalse()
    {
        var form = CreateTestForm();
        form.AllowedOrigins = JsonSerializer.Serialize(new[] { "https://example.com" });

        Assert.False(Beacon.Api.SubmissionEndpoints.IsRedirectAllowedForForm(form, "https://evil.com/phish"));
    }

    [Fact]
    public void IsRedirectAllowedForForm_NullUrl_ReturnsFalse()
    {
        var form = CreateTestForm();
        form.AllowedOrigins = JsonSerializer.Serialize(new[] { "https://example.com" });

        Assert.False(Beacon.Api.SubmissionEndpoints.IsRedirectAllowedForForm(form, null));
    }

    [Fact]
    public void IsRedirectAllowedForForm_JavascriptScheme_ReturnsFalse()
    {
        var form = CreateTestForm();
        form.AllowedOrigins = JsonSerializer.Serialize(new[] { "https://example.com" });

        Assert.False(Beacon.Api.SubmissionEndpoints.IsRedirectAllowedForForm(form, "javascript:alert(1)"));
    }

    // --- Helpers ---

    private (SubmissionFormService service, InMemorySubmissionFormRepository formRepo, InMemoryConsentRepository consentRepo, EmailHasher emailHasher) CreateFullService()
    {
        var formRepo = new InMemorySubmissionFormRepository();
        var consentRepo = new InMemoryConsentRepository();
        var webhookService = new InMemoryWebhookService();
        var emailHasher = new EmailHasher(TestPepper);
        var encryptor = new Encryptor(TestEncryptionKey);
        var consentService = new ConsentService(consentRepo, emailHasher, encryptor);
        var service = new SubmissionFormService(formRepo, consentService, consentRepo, webhookService, encryptor, emailHasher);
        return (service, formRepo, consentRepo, emailHasher);
    }

    private static SubmissionForm CreateTestForm(string name = "Test Form")
    {
        return new SubmissionForm
        {
            Name = name,
            Bucket = "test-bucket",
            Permission = "newsletter",
            AllowedOrigins = JsonSerializer.Serialize(new[] { "https://example.com" }),
            EncryptedApiToken = "placeholder"
        };
    }

    // --- In-memory test doubles ---

    internal class InMemorySubmissionFormRepository : ISubmissionFormRepository
    {
        public List<SubmissionForm> Forms { get; } = new();

        public Task<SubmissionForm?> GetByIdAsync(Guid id)
        {
            return Task.FromResult(Forms.FirstOrDefault(f => f.Id == id));
        }

        public Task<List<SubmissionForm>> GetAllAsync()
        {
            return Task.FromResult(Forms.ToList());
        }

        public Task CreateAsync(SubmissionForm form)
        {
            Forms.Add(form);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(SubmissionForm form)
        {
            var index = Forms.FindIndex(f => f.Id == form.Id);
            if (index >= 0)
                Forms[index] = form;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid id)
        {
            Forms.RemoveAll(f => f.Id == id);
            return Task.CompletedTask;
        }

        public Task IncrementSubmissionCountAsync(Guid id)
        {
            var form = Forms.FirstOrDefault(f => f.Id == id);
            if (form is not null)
                form.SubmissionCount++;
            return Task.CompletedTask;
        }
    }

    internal class InMemoryConsentRepository : IConsentRepository
    {
        private readonly Dictionary<string, ConsentRecord> _records = new();

        public Task<ConsentRecord?> GetAsync(string bucket, string emailHash, string permission)
        {
            var key = $"{bucket}:{emailHash}:{permission}";
            _records.TryGetValue(key, out var record);
            return Task.FromResult(record);
        }

        public Task UpsertAsync(ConsentRecord record)
        {
            var key = $"{record.Bucket}:{record.EmailHash}:{record.Permission}";
            _records[key] = record;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<BucketInfo>> GetBucketsAsync()
        {
            return Task.FromResult<IReadOnlyList<BucketInfo>>(new List<BucketInfo>());
        }

        public Task<BucketDetails> GetBucketDetailsAsync(string bucket)
        {
            return Task.FromResult(new BucketDetails { Name = bucket, Permissions = [], Stats = [] });
        }

        public Task<PagedResult<EmailPermissions>> GetBucketRecordsAsync(string bucket, int page, int pageSize, string? sortBy = null, string? sortDir = null, string? search = null)
        {
            return Task.FromResult(new PagedResult<EmailPermissions> { Records = [], Total = 0, Page = page, PageSize = pageSize });
        }

        public Task<IReadOnlyList<EmailPermissions>> GetAllBucketRecordsAsync(string bucket)
        {
            return Task.FromResult<IReadOnlyList<EmailPermissions>>(new List<EmailPermissions>());
        }

        public Task<int> DeleteRecordAsync(string bucket, string emailHash) => Task.FromResult(0);
        public Task<int> DeleteBucketAsync(string bucket) => Task.FromResult(0);
        public Task<bool> EmailExistsInBucketAsync(string bucket, string emailHash) => Task.FromResult(false);

        public Task<IReadOnlyList<ConsentRecord>> GetByEmailAsync(string bucket, string emailHash)
        {
            var records = _records.Values
                .Where(r => r.Bucket == bucket && r.EmailHash == emailHash)
                .ToList();
            return Task.FromResult<IReadOnlyList<ConsentRecord>>(records);
        }
    }

    internal class InMemoryWebhookService : IWebhookService
    {
        public List<WebhookTriggerData> TriggeredWebhooks { get; } = new();

        public Task<WebhookConfig?> GetWebhookConfigAsync(string bucket)
        {
            return Task.FromResult<WebhookConfig?>(null);
        }

        public Task<string> SaveWebhookConfigAsync(string bucket, string url, string method, Dictionary<string, string>? headers, string? bodyTemplate)
        {
            return Task.FromResult("test-id");
        }

        public Task DeleteWebhookConfigAsync(string bucket) => Task.CompletedTask;

        public Task TriggerWebhookAsync(string bucket, WebhookTriggerData data)
        {
            TriggeredWebhooks.Add(data);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> GetWebhookBucketsAsync()
        {
            return Task.FromResult<IReadOnlyList<string>>(new List<string>());
        }
    }
}
