using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Beacon.Storage;

public sealed class ConsentAuditBackfillService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ConsentAuditBackfillService> _logger;

    public ConsentAuditBackfillService(
        IServiceScopeFactory scopeFactory,
        ILogger<ConsentAuditBackfillService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BeaconDbContext>();

        using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, stoppingToken);

        var auditCount = await db.Database
            .SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM ConsentAuditEntries")
            .SingleAsync(stoppingToken);

        if (auditCount > 0)
        {
            await tx.RollbackAsync(stoppingToken);
            return;
        }

        var consentCount = await db.ConsentRecords.CountAsync(stoppingToken);
        if (consentCount == 0)
        {
            await tx.RollbackAsync(stoppingToken);
            return;
        }

        _logger.LogInformation(
            "Audit backfill: populating {Count} consent records into ConsentAuditEntries",
            consentCount);

        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO ConsentAuditEntries
                (Id, Bucket, EmailHash, Permission, OldStatus, NewStatus, Source, ActorId, ChangedAt)
            SELECT
                lower(
                    hex(randomblob(4)) || '-' ||
                    hex(randomblob(2)) || '-4' ||
                    substr(hex(randomblob(2)), 2) || '-' ||
                    substr('89ab', (abs(random()) % 4) + 1, 1) ||
                    substr(hex(randomblob(2)), 2) || '-' ||
                    hex(randomblob(6))
                ),
                Bucket, EmailHash, Permission,
                NULL, Status, Source, NULL, ChangedAt
            FROM ConsentRecords
            """, stoppingToken);

        await tx.CommitAsync(stoppingToken);
        _logger.LogInformation("Audit backfill: complete");
    }
}
