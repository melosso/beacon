using Beacon.Core.Models;
using Beacon.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Storage;

public sealed class EmailQueueRepository : IEmailQueueRepository
{
    private readonly BeaconDbContext _db;

    public EmailQueueRepository(BeaconDbContext db) => _db = db;

    public async Task EnqueueAsync(EmailQueueEntry entry)
    {
        _db.EmailQueueEntries.Add(entry);
        await _db.SaveChangesAsync();
    }

    public async Task<List<EmailQueueEntry>> GetPendingBatchAsync(int batchSize = 50)
    {
        var now = DateTime.UtcNow;
        return await _db.EmailQueueEntries
            .Where(e => e.Status == EmailQueueStatus.Pending
                     && (e.NextAttemptAt == null || e.NextAttemptAt <= now)
                     && e.ExpiresAt > now)
            .OrderBy(e => e.CreatedAt)
            .Take(batchSize)
            .ToListAsync();
    }

    public async Task MarkSentAsync(Guid id, DateTime sentAt)
    {
        await _db.EmailQueueEntries
            .Where(e => e.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.Status, EmailQueueStatus.Sent)
                .SetProperty(e => e.SentAt, sentAt)
                .SetProperty(e => e.AttemptCount, e => e.AttemptCount + 1));
    }

    public async Task MarkFailedAsync(Guid id, string error, DateTime? nextAttemptAt)
    {
        await _db.EmailQueueEntries
            .Where(e => e.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.Status, nextAttemptAt.HasValue ? EmailQueueStatus.Pending : EmailQueueStatus.Failed)
                .SetProperty(e => e.LastError, error)
                .SetProperty(e => e.NextAttemptAt, nextAttemptAt)
                .SetProperty(e => e.AttemptCount, e => e.AttemptCount + 1));
    }

    public async Task<EmailQueueEntry?> GetByConfirmationTokenAsync(string token)
        => await _db.EmailQueueEntries.FirstOrDefaultAsync(e => e.ConfirmationToken == token);

    public async Task MarkConfirmedAsync(Guid id, DateTime confirmedAt)
    {
        await _db.EmailQueueEntries
            .Where(e => e.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.Status, EmailQueueStatus.Confirmed)
                .SetProperty(e => e.ConfirmedAt, confirmedAt));
    }

    public async Task<bool> HasPendingAsync(string bucket, string emailHash, string permission)
        => await _db.EmailQueueEntries.AnyAsync(e =>
            e.Bucket == bucket &&
            e.EmailHash == emailHash &&
            e.Permission == permission &&
            (e.Status == EmailQueueStatus.Pending || e.Status == EmailQueueStatus.Sent));

    public async Task CancelPendingAsync(string bucket, string emailHash, string permission)
    {
        await _db.EmailQueueEntries
            .Where(e => e.Bucket == bucket &&
                        e.EmailHash == emailHash &&
                        e.Permission == permission &&
                        e.Status == EmailQueueStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.Status, EmailQueueStatus.Cancelled));
    }

    public async Task PruneExpiredAsync()
    {
        var now = DateTime.UtcNow;
        await _db.EmailQueueEntries
            .Where(e => e.ExpiresAt <= now && e.Status != EmailQueueStatus.Confirmed)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.Status, EmailQueueStatus.Expired));
    }
}
