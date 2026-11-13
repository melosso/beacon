using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Beacon.Api;
using Beacon.Core.Models;
using Beacon.Core.Security;
using Beacon.Core.Services;
using Beacon.Security;
using Beacon.Storage;
using Beacon.Tests.Fakes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Beacon.Tests;

public sealed class StateMachineTests : IDisposable
{
    private const string Pepper = "test-pepper-for-hashing";
    private const string EncryptionKey = "MDEyMzQ1Njc4OTAxMjM0NTY3ODkwMTIzNDU2Nzg5MDE=";
    private const string Bucket = "b";
    private const string Email = "user@example.com";
    private const string Permission = "newsletter";

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<BeaconDbContext> _options;
    private readonly BeaconDbContext _db;
    private readonly EmailHasher _hasher = new(Pepper);
    private readonly Encryptor _encryptor = new(EncryptionKey);

    public StateMachineTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<BeaconDbContext>().UseSqlite(_connection).Options;
        _db = new BeaconDbContext(_options);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ConsentService Consent(IConsentRepository? repo = null, BeaconDbContext? db = null) =>
        new(repo ?? new ConsentRepository(db ?? _db), new NullBeaconCacheService(), new StubSystemConfigurationService(), _hasher, _encryptor);

    private async Task<ConsentStatus?> StoredStatusAsync()
    {
        _db.ChangeTracker.Clear();
        return (await _db.ConsentRecords.AsNoTracking().FirstOrDefaultAsync(Ct))?.Status;
    }

    // Consent record

    [Fact]
    public async Task PendingPurge_KeepsOptOut()
    {
        var consent = Consent();
        await consent.OverrideAsync(Bucket, Email, Permission, ConsentStatus.OptedOut);
        await consent.OverrideAsync(Bucket, Email, Permission, ConsentStatus.PendingConfirmation);

        await new ConsentRepository(_db).PurgePendingConfirmationAsync(DateTime.UtcNow.AddSeconds(1), Ct);
        _db.ChangeTracker.Clear();

        Assert.Equal(ConsentStatus.OptedOut, await consent.CheckAsync(Bucket, Email, Permission));
    }

    [Fact]
    public async Task PendingWrite_DoesNotDowngradeOptIn()
    {
        var consent = Consent();
        await consent.OverrideAsync(Bucket, Email, Permission, ConsentStatus.OptedIn);
        await consent.OverrideAsync(Bucket, Email, Permission, ConsentStatus.PendingConfirmation);

        Assert.Equal(ConsentStatus.OptedIn, await StoredStatusAsync());
    }

    [Fact]
    public async Task Ensure_StaleRead_DoesNotOverwrite()
    {
        await Consent().OverrideAsync(Bucket, Email, Permission, ConsentStatus.OptedOut);
        _db.ChangeTracker.Clear();

        // Simulates a concurrent insert between EnsureAsync's read and its write.
        var staleRead = Proxy<IConsentRepository>.Create(new ConsentRepository(_db),
            (m, _) => m.Name == nameof(IConsentRepository.GetAsync) ? (true, Task.FromResult<ConsentRecord?>(null)) : (false, null));
        await Consent(staleRead).EnsureAsync(Bucket, Email, Permission, ConsentStatus.OptedIn);

        Assert.Equal(ConsentStatus.OptedOut, await StoredStatusAsync());
    }

    [Theory]
    [InlineData(null, ConsentStatus.PendingConfirmation, true)]
    [InlineData(ConsentStatus.OptedOut, ConsentStatus.PendingConfirmation, true)]
    [InlineData(ConsentStatus.PendingConfirmation, ConsentStatus.PendingConfirmation, true)]
    [InlineData(ConsentStatus.OptedIn, ConsentStatus.PendingConfirmation, false)]
    [InlineData(ConsentStatus.OptedIn, ConsentStatus.OptedOut, true)]
    [InlineData(ConsentStatus.PendingConfirmation, ConsentStatus.OptedIn, true)]
    [InlineData(ConsentStatus.OptedOut, ConsentStatus.OptedIn, true)]
    public void Transition_Table(ConsentStatus? from, ConsentStatus to, bool allowed) =>
        Assert.Equal(allowed, ConsentTransition.IsAllowed(from, to));

    [Fact]
    public async Task PendingPurge_AnonymisesAndAudits()
    {
        var consent = Consent();
        await consent.OverrideAsync(Bucket, Email, Permission, ConsentStatus.PendingConfirmation, name: "Ann", consentText: "t");

        var purged = await new ConsentRepository(_db).PurgePendingConfirmationAsync(DateTime.UtcNow.AddSeconds(1), Ct);
        _db.ChangeTracker.Clear();

        Assert.Equal(1, purged);
        var record = await _db.ConsentRecords.AsNoTracking().SingleAsync(Ct);
        Assert.Null(record.EncryptedEmail);
        Assert.Null(record.EncryptedName);
        Assert.Null(record.ConsentText);
        var audit = await _db.ConsentAuditEntries.AsNoTracking().OrderBy(a => a.ChangedAt).ToListAsync(Ct);
        Assert.Equal(ConsentStatus.PendingConfirmation, audit[^1].OldStatus);
        Assert.Equal(ConsentStatus.OptedOut, audit[^1].NewStatus);
    }

    [Fact]
    public async Task PendingPurge_IgnoresNewerRecords()
    {
        await Consent().OverrideAsync(Bucket, Email, Permission, ConsentStatus.PendingConfirmation);

        Assert.Equal(0, await new ConsentRepository(_db).PurgePendingConfirmationAsync(DateTime.UtcNow.AddDays(-1), Ct));
        Assert.Equal(ConsentStatus.PendingConfirmation, await StoredStatusAsync());
    }

    [Fact]
    public async Task PendingPurge_RenewedAfterRead_KeepsRecord()
    {
        await Consent().OverrideAsync(Bucket, Email, Permission, ConsentStatus.PendingConfirmation);
        var renew = new RenewBeforeUpdate();
        await using var db = new BeaconDbContext(new DbContextOptionsBuilder<BeaconDbContext>().UseSqlite(_connection).AddInterceptors(renew).Options);

        var purged = await new ConsentRepository(db).PurgePendingConfirmationAsync(DateTime.UtcNow.AddSeconds(1), Ct);

        Assert.True(renew.Fired);
        Assert.Equal(0, purged);
        _db.ChangeTracker.Clear();
        var record = await _db.ConsentRecords.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(ConsentStatus.PendingConfirmation, record.Status);
        Assert.NotNull(record.EncryptedEmail);
    }

    private sealed class RenewBeforeUpdate : Microsoft.EntityFrameworkCore.Diagnostics.DbCommandInterceptor
    {
        public bool Fired { get; private set; }

        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> NonQueryExecutingAsync(
            System.Data.Common.DbCommand command,
            Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (!Fired && command.CommandText.StartsWith("UPDATE", StringComparison.Ordinal))
            {
                Fired = true;
                eventData.Context!.Set<ConsentRecord>().ExecuteUpdate(s => s.SetProperty(r => r.ChangedAt, DateTime.UtcNow.AddMinutes(1)));
            }
            return ValueTask.FromResult(result);
        }
    }

    private static ConsentRecord Record(ConsentStatus status) => new()
    {
        Id = Guid.NewGuid(), Bucket = Bucket, EmailHash = "h", Permission = Permission, Status = status, ChangedAt = DateTime.UtcNow
    };

    [Theory]
    [InlineData(ConsentStatus.OptedOut, ConsentStatus.OptedIn, true, false, ConsentStatus.OptedOut)]
    [InlineData(ConsentStatus.OptedOut, ConsentStatus.OptedIn, false, true, ConsentStatus.OptedIn)]
    [InlineData(ConsentStatus.OptedIn, ConsentStatus.PendingConfirmation, false, false, ConsentStatus.OptedIn)]
    public async Task Upsert_LostInsertRace_AppliesGuard(ConsentStatus winner, ConsentStatus incoming, bool insertOnly, bool expectWrite, ConsentStatus expected)
    {
        var race = new RaceBeforeSave(() =>
        {
            using var other = new BeaconDbContext(_options);
            other.ConsentRecords.Add(Record(winner));
            other.SaveChanges();
        });
        await using var db = new BeaconDbContext(new DbContextOptionsBuilder<BeaconDbContext>().UseSqlite(_connection).AddInterceptors(race).Options);

        var wrote = await new ConsentRepository(db).UpsertAsync(Record(incoming), insertOnly: insertOnly);

        Assert.True(race.Fired);
        Assert.Equal(expectWrite, wrote);
        Assert.Equal(expected, await StoredStatusAsync());
    }

    [Fact]
    public async Task Upsert_StaleStatus_RechecksTransition()
    {
        await new ConsentRepository(_db).UpsertAsync(Record(ConsentStatus.OptedOut));
        var race = new RaceBeforeSave(() =>
        {
            using var other = new BeaconDbContext(_options);
            other.ConsentRecords.ExecuteUpdate(s => s.SetProperty(r => r.Status, ConsentStatus.OptedIn));
        });
        await using var db = new BeaconDbContext(new DbContextOptionsBuilder<BeaconDbContext>().UseSqlite(_connection).AddInterceptors(race).Options);

        var wrote = await new ConsentRepository(db).UpsertAsync(Record(ConsentStatus.PendingConfirmation));

        Assert.True(race.Fired);
        Assert.False(wrote);
        Assert.Equal(ConsentStatus.OptedIn, await StoredStatusAsync());
    }

    private sealed class RaceBeforeSave(Action race) : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        public bool Fired { get; private set; }

        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (!Fired) { Fired = true; race(); }
            return ValueTask.FromResult(result);
        }
    }

    // Workflow task

    private async Task<WorkflowTask> SeedApprovalTaskAsync()
    {
        var task = new WorkflowTask
        {
            Id = Guid.NewGuid(),
            TaskType = WorkflowTaskType.RetentionPurge,
            Status = WorkflowTaskStatus.PendingApproval,
            ScheduledAt = DateTime.UtcNow
        };
        await new WorkflowTaskRepository(_db).CreateAsync(task, Ct);
        _db.ChangeTracker.Clear();
        return task;
    }

    private async Task<WorkflowTask> ReadTaskAsync(Guid id)
    {
        await using var db = new BeaconDbContext(_options);
        return await db.WorkflowTasks.AsNoTracking().SingleAsync(t => t.Id == id, Ct);
    }

    [Fact]
    public async Task Approve_Completes()
    {
        var task = await SeedApprovalTaskAsync();
        var svc = new DataPolicyService(new WorkflowTaskRepository(_db), new ConsentRepository(_db), new StubSystemConfigurationService());

        var result = await svc.ApproveTaskAsync(task.Id, Ct);

        Assert.Equal(TaskOperationOutcome.Success, result.Outcome);
        Assert.Equal(WorkflowTaskStatus.Completed, (await ReadTaskAsync(task.Id)).Status);
    }

    [Fact]
    public async Task Approve_Cancelled_MarksFailed()
    {
        var task = await SeedApprovalTaskAsync();
        var cancelling = Proxy<IConsentRepository>.Create(new ConsentRepository(_db),
            (_, _) => throw new OperationCanceledException());
        var svc = new DataPolicyService(new WorkflowTaskRepository(_db), cancelling, new StubSystemConfigurationService());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => svc.ApproveTaskAsync(task.Id, Ct));

        Assert.Equal(WorkflowTaskStatus.Failed, (await ReadTaskAsync(task.Id)).Status);
    }

    [Fact]
    public async Task Approve_Concurrent_RunsOnce()
    {
        var task = await SeedApprovalTaskAsync();
        var runs = 0;
        IConsentRepository Counting(BeaconDbContext db) => Proxy<IConsentRepository>.Create(new ConsentRepository(db),
            (m, _) => { if (m.Name == nameof(IConsentRepository.AnonymiseOptedOutAsync)) Interlocked.Increment(ref runs); return (false, null); });

        await using var dbA = new BeaconDbContext(_options);
        await using var dbB = new BeaconDbContext(_options);
        await dbB.WorkflowTasks.FindAsync([task.Id], Ct); // B holds the pre-approval snapshot.

        var a = await new DataPolicyService(new WorkflowTaskRepository(dbA), Counting(dbA), new StubSystemConfigurationService()).ApproveTaskAsync(task.Id, Ct);
        var b = await new DataPolicyService(new WorkflowTaskRepository(dbB), Counting(dbB), new StubSystemConfigurationService()).ApproveTaskAsync(task.Id, Ct);

        Assert.Equal(TaskOperationOutcome.Success, a.Outcome);
        Assert.Equal(TaskOperationOutcome.InvalidStatus, b.Outcome);
        Assert.Equal(1, runs);
    }

    private static SystemConfig RetentionConfig(bool requireApproval, int days = -1) => new()
    {
        DataPoliciesEnabled = true,
        RetentionPurgeEnabled = true,
        RetentionPurgeDays = days,
        RetentionPurgeRequireApproval = requireApproval
    };

    private async Task<DataPolicyWorker> WorkerAsync(bool requireApproval, ISystemConfigurationService? configSvc = null)
    {
        await Consent().OverrideAsync(Bucket, Email, Permission, ConsentStatus.OptedOut);
        _db.ChangeTracker.Clear();
        var services = new ServiceCollection()
            .AddScoped(_ => new BeaconDbContext(_options))
            .AddScoped<IConsentRepository, ConsentRepository>()
            .AddScoped<WorkflowTaskRepository>()
            .AddScoped<DataPolicyService>()
            .AddSingleton(configSvc ?? new StubSystemConfigurationService(RetentionConfig(requireApproval)))
            .BuildServiceProvider();
        return new DataPolicyWorker(services.GetRequiredService<IServiceScopeFactory>(), NullLogger<DataPolicyWorker>.Instance, new DataPolicyTrigger());
    }

    [Fact]
    public async Task Worker_SettingsSavedMidBatch_UsesBatchSnapshot()
    {
        var reads = 0;
        var saved = RetentionConfig(requireApproval: true, days: 3650);
        var configSvc = Proxy<ISystemConfigurationService>.Create(new StubSystemConfigurationService(RetentionConfig(requireApproval: false)),
            (m, _) => m.Name == nameof(ISystemConfigurationService.Get) && Interlocked.Increment(ref reads) > 1 ? (true, saved) : (false, null));
        var worker = await WorkerAsync(requireApproval: false, configSvc);

        await worker.ProcessBatchAsync(WorkflowTrigger.Cron, isManual: false, Ct);

        await using var db = new BeaconDbContext(_options);
        var task = await db.WorkflowTasks.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(WorkflowTaskStatus.Completed, task.Status);
        Assert.Equal(-1, task.PolicyDays);
        Assert.Equal(1, task.RecordsAffected);
    }

    [Fact]
    public async Task Approve_UsesPolicyDaysStoredAtScheduling()
    {
        var worker = await WorkerAsync(requireApproval: true);
        await worker.ProcessBatchAsync(WorkflowTrigger.Cron, isManual: false, Ct);
        var task = await _db.WorkflowTasks.AsNoTracking().SingleAsync(Ct);
        DateTime? cutoff = null;
        var repo = Proxy<IConsentRepository>.Create(new ConsentRepository(_db),
            (m, args) => { if (m.Name == nameof(IConsentRepository.AnonymiseOptedOutAsync)) cutoff = (DateTime)args![0]!; return (false, null); });
        var svc = new DataPolicyService(new WorkflowTaskRepository(_db), repo, new StubSystemConfigurationService(RetentionConfig(requireApproval: true, days: 3650)));

        await svc.ApproveTaskAsync(task.Id, Ct);

        Assert.Equal(-1, task.PolicyDays);
        Assert.Equal(task.ScheduledAt.AddDays(1), cutoff);
    }

    [Fact]
    public void BulkReadEndpoints_RequireExportRateLimit()
    {
        var app = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder().Build();
        app.MapAdminEndpoints();

        var bulk = ((Microsoft.AspNetCore.Routing.IEndpointRouteBuilder)app).DataSources
            .SelectMany(d => d.Endpoints)
            .OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText is { } route
                && (route.EndsWith("/export", StringComparison.Ordinal) || route == "/api/bucket/{bucket}/records"))
            .ToList();

        Assert.Contains(bulk, e => e.RoutePattern.RawText == "/api/bucket/{bucket}/records");
        Assert.All(bulk, e => Assert.Equal("export", e.Metadata.GetMetadata<Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute>()?.PolicyName));
    }

    [Fact]
    public async Task Worker_RepeatedTicks_QueueOneApproval()
    {
        var worker = await WorkerAsync(requireApproval: true);

        await worker.ProcessBatchAsync(WorkflowTrigger.Cron, isManual: false, Ct);
        await worker.ProcessBatchAsync(WorkflowTrigger.Cron, isManual: false, Ct);

        await using var db = new BeaconDbContext(_options);
        Assert.Equal(1, await db.WorkflowTasks.CountAsync(t => t.Status == WorkflowTaskStatus.PendingApproval, Ct));
    }

    [Fact]
    public async Task Worker_AutoRun_Completes()
    {
        var worker = await WorkerAsync(requireApproval: false);

        await worker.ProcessBatchAsync(WorkflowTrigger.Cron, isManual: false, Ct);

        await using var db = new BeaconDbContext(_options);
        var task = await db.WorkflowTasks.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(WorkflowTaskStatus.Completed, task.Status);
        Assert.Equal(1, task.RecordsAffected);
    }

    [Fact]
    public async Task TaskTransition_SecondClaim_ReturnsFalse()
    {
        var task = await SeedApprovalTaskAsync();
        await using var dbA = new BeaconDbContext(_options);
        await using var dbB = new BeaconDbContext(_options);
        var running = task with { Status = WorkflowTaskStatus.Running };

        Assert.True(await new WorkflowTaskRepository(dbA).TransitionAsync(WorkflowTaskStatus.PendingApproval, running, Ct));
        Assert.False(await new WorkflowTaskRepository(dbB).TransitionAsync(WorkflowTaskStatus.PendingApproval, running, Ct));
    }

    [Fact]
    public async Task Reject_AfterApprove_InvalidStatus()
    {
        var task = await SeedApprovalTaskAsync();
        var svc = new DataPolicyService(new WorkflowTaskRepository(_db), new ConsentRepository(_db), new StubSystemConfigurationService());
        await svc.ApproveTaskAsync(task.Id, Ct);

        Assert.Equal(TaskOperationOutcome.InvalidStatus, (await svc.RejectTaskAsync(task.Id, Ct)).Outcome);
        Assert.Equal(WorkflowTaskStatus.Completed, (await ReadTaskAsync(task.Id)).Status);
    }

    // Email queue

    private static string Sha(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();

    private async Task<EmailQueueEntry> SeedEntryAsync(string plain, EmailQueueStatus status, DateTime? expiresAt = null)
    {
        var entry = new EmailQueueEntry
        {
            Bucket = Bucket,
            EncryptedEmail = _encryptor.Encrypt(Email),
            EmailHash = _hasher.Hash(Email),
            Permission = Permission,
            ConfirmationToken = Sha(plain),
            ConfirmationUrl = $"https://x/confirm/{plain}",
            ExpiresAt = expiresAt ?? DateTime.UtcNow.AddDays(7),
            Status = status
        };
        await new EmailQueueRepository(_db).EnqueueAsync(entry);
        _db.ChangeTracker.Clear();
        return entry;
    }

    private async Task<EmailQueueStatus> EntryStatusAsync(Guid id)
    {
        await using var db = new BeaconDbContext(_options);
        return (await db.EmailQueueEntries.AsNoTracking().SingleAsync(e => e.Id == id, Ct)).Status;
    }

    private Task<IResult> ConfirmAsync(string plain) =>
        ConsentEndpoints.ConfirmSubscription(plain, Ct, new EmailQueueRepository(_db), Consent(),
            _encryptor, new BrandIdentityService(new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>()),
            new BucketRepository(_db, new NullBeaconCacheService(), new StubSystemConfigurationService()));

    [Fact]
    public async Task Confirm_Sent_OptsIn()
    {
        await Consent().OverrideAsync(Bucket, Email, Permission, ConsentStatus.PendingConfirmation);
        var entry = await SeedEntryAsync("tk", EmailQueueStatus.Sent);

        await ConfirmAsync("tk");

        Assert.Equal(ConsentStatus.OptedIn, await StoredStatusAsync());
        Assert.Equal(EmailQueueStatus.Confirmed, await EntryStatusAsync(entry.Id));
    }

    [Fact]
    public async Task Confirm_Cancelled_DoesNotOptIn()
    {
        await Consent().OverrideAsync(Bucket, Email, Permission, ConsentStatus.PendingConfirmation);
        await SeedEntryAsync("tk", EmailQueueStatus.Cancelled);

        await ConfirmAsync("tk");

        Assert.Equal(ConsentStatus.PendingConfirmation, await StoredStatusAsync());
    }

    [Fact]
    public async Task Confirm_ArchivedBucket_DoesNotOptIn()
    {
        await Consent().OverrideAsync(Bucket, Email, Permission, ConsentStatus.PendingConfirmation);
        await SeedEntryAsync("tk", EmailQueueStatus.Sent);
        await new BucketRepository(_db, new NullBeaconCacheService(), new StubSystemConfigurationService()).ArchiveAsync(Bucket);

        await ConfirmAsync("tk");

        Assert.Equal(ConsentStatus.PendingConfirmation, await StoredStatusAsync());
    }

    [Fact]
    public async Task Confirm_Twice_SecondIsNoOp()
    {
        var entry = await SeedEntryAsync("tk", EmailQueueStatus.Sent);
        await ConfirmAsync("tk");
        await Consent().OverrideAsync(Bucket, Email, Permission, ConsentStatus.OptedOut);

        await ConfirmAsync("tk");

        Assert.Equal(ConsentStatus.OptedOut, await StoredStatusAsync());
        Assert.Equal(EmailQueueStatus.Confirmed, await EntryStatusAsync(entry.Id));
    }

    [Fact]
    public async Task Confirm_Expired_DoesNotOptIn()
    {
        await Consent().OverrideAsync(Bucket, Email, Permission, ConsentStatus.PendingConfirmation);
        await SeedEntryAsync("tk", EmailQueueStatus.Sent, DateTime.UtcNow.AddSeconds(-1));

        await ConfirmAsync("tk");

        Assert.Equal(ConsentStatus.PendingConfirmation, await StoredStatusAsync());
    }

    [Theory]
    [InlineData(EmailQueueStatus.Sent)]
    [InlineData(EmailQueueStatus.Failed)]
    public async Task CancelPending_CancelsConfirmable(EmailQueueStatus status)
    {
        var entry = await SeedEntryAsync("tk", status);

        await new EmailQueueRepository(_db).CancelPendingAsync(Bucket, _hasher.Hash(Email), Permission);

        Assert.Equal(EmailQueueStatus.Cancelled, await EntryStatusAsync(entry.Id));
    }

    [Fact]
    public async Task WorkerResult_DoesNotReviveCancelled()
    {
        var sent = await SeedEntryAsync("tk1", EmailQueueStatus.Cancelled);
        var failed = await SeedEntryAsync("tk2", EmailQueueStatus.Cancelled);
        var queue = new EmailQueueRepository(_db);

        await queue.MarkSentAsync(sent.Id, DateTime.UtcNow);
        await queue.MarkFailedAsync(failed.Id, "x", DateTime.UtcNow.AddMinutes(5));

        Assert.Equal(EmailQueueStatus.Cancelled, await EntryStatusAsync(sent.Id));
        Assert.Equal(EmailQueueStatus.Cancelled, await EntryStatusAsync(failed.Id));
    }

    [Theory]
    [InlineData(EmailQueueStatus.Cancelled)]
    [InlineData(EmailQueueStatus.Failed)]
    [InlineData(EmailQueueStatus.Confirmed)]
    public async Task PruneExpired_KeepsEndStates(EmailQueueStatus status)
    {
        var entry = await SeedEntryAsync("tk", status, DateTime.UtcNow.AddDays(-1));

        await new EmailQueueRepository(_db).PruneExpiredAsync();

        Assert.Equal(status, await EntryStatusAsync(entry.Id));
    }

    // Single-use token

    [Fact]
    public async Task TryMarkUsed_SecondClaim_ReturnsFalse()
    {
        await using var dbA = new BeaconDbContext(_options);
        await using var dbB = new BeaconDbContext(_options);

        Assert.True(await new TokenUsageRepository(dbA).TryMarkUsedAsync("h", DateTime.UtcNow.AddDays(1)));
        Assert.False(await new TokenUsageRepository(dbB).TryMarkUsedAsync("h", DateTime.UtcNow.AddDays(1)));
        Assert.Empty(dbB.ChangeTracker.Entries());
    }

    // Session

    [Fact]
    public async Task Refresh_RevokesPresentedToken()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var now = DateTimeOffset.UtcNow;
        var token = JwtAuthHandler.CreateToken(key, "admin", now.AddHours(1), "admin", authTime: now);
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers.Cookie = $"{JwtAuthHandler.CookieName}={token}";
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Name, "admin"),
            new Claim(ClaimTypes.Role, "admin"),
            new Claim(JwtAuthHandler.AuthTimeClaim, now.ToUnixTimeSeconds().ToString())
        ], JwtAuthExtensions.SchemeName));
        var usage = new TokenUsageRepository(_db);

        await AuthEndpoints.Refresh(ctx, new StubEnv(), new StubOptions(new JwtAuthOptions { SigningKey = key }), usage);

        Assert.True(await usage.IsTokenUsedAsync(JwtAuthHandler.SessionHash(token)));
        var issued = ctx.Response.Headers.SetCookie.ToString().Split(';')[0][(JwtAuthHandler.CookieName.Length + 1)..];
        Assert.NotEqual(token, issued);
        Assert.False(await usage.IsTokenUsedAsync(JwtAuthHandler.SessionHash(issued)));

        var replay = await AuthEndpoints.Refresh(ctx, new StubEnv(), new StubOptions(new JwtAuthOptions { SigningKey = key }), usage);
        Assert.Equal(StatusCodes.Status409Conflict, Assert.IsAssignableFrom<IStatusCodeHttpResult>(replay).StatusCode);
    }

    [Fact]
    public void CreateToken_SameSecond_IsUnique()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var exp = DateTimeOffset.UtcNow.AddHours(1);
        Assert.NotEqual(JwtAuthHandler.CreateToken(key, "admin", exp), JwtAuthHandler.CreateToken(key, "admin", exp));
    }

    private sealed class StubOptions(JwtAuthOptions value) : IOptionsMonitor<JwtAuthOptions>
    {
        public JwtAuthOptions CurrentValue => value;
        public JwtAuthOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<JwtAuthOptions, string?> listener) => null;
    }

    private sealed class StubEnv : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = "";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "Beacon";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = "";
        public string EnvironmentName { get; set; } = "Development";
    }
}

/// <summary>Forwards interface calls to a target; an intercept returning (true, value) replaces the call.</summary>
public class Proxy<T> : DispatchProxy where T : class
{
    private T _target = null!;
    private Func<MethodInfo, object?[]?, (bool Handled, object? Result)> _intercept = null!;

    public static T Create(T target, Func<MethodInfo, object?[]?, (bool, object?)> intercept)
    {
        var proxy = DispatchProxy.Create<T, Proxy<T>>();
        var p = (Proxy<T>)(object)proxy;
        p._target = target;
        p._intercept = intercept;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        var (handled, result) = _intercept(method!, args);
        if (handled) return result;
        try { return method!.Invoke(_target, args); }
        catch (TargetInvocationException ex) when (ex.InnerException is not null) { throw ex.InnerException; }
    }
}

public sealed class SqliteMigratorTests
{
    [Fact]
    public void Initialize_RestoresMissingColumnAndTable()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var db = new BeaconDbContext(new DbContextOptionsBuilder<BeaconDbContext>().UseSqlite(connection).Options);
        db.Database.EnsureCreated();
        db.Database.ExecuteSqlRaw("ALTER TABLE ConsentRecords DROP COLUMN EncryptedName");
        db.Database.ExecuteSqlRaw("ALTER TABLE WorkflowTasks DROP COLUMN PolicyDays");
        db.Database.ExecuteSqlRaw("DROP TABLE ArchivedBuckets");

        DatabaseMigrator.Initialize(db);
        DatabaseMigrator.Initialize(db);

        Assert.Contains("EncryptedName", db.Database.SqlQueryRaw<string>("SELECT name AS Value FROM pragma_table_info('ConsentRecords')").ToList());
        Assert.Contains("PolicyDays", db.Database.SqlQueryRaw<string>("SELECT name AS Value FROM pragma_table_info('WorkflowTasks')").ToList());
        Assert.Equal(1, db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM sqlite_master WHERE type='table' AND name='ArchivedBuckets'").AsEnumerable().Single());
    }
}
