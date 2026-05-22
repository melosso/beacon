using Beacon.Core.Models;

namespace Beacon.Core.Services;

public interface IEmailQueueRepository
{
    Task EnqueueAsync(EmailQueueEntry entry);
    Task<List<EmailQueueEntry>> GetPendingBatchAsync(int batchSize = 50);
    Task MarkSentAsync(Guid id, DateTime sentAt);
    Task MarkFailedAsync(Guid id, string error, DateTime? nextAttemptAt);
    Task<EmailQueueEntry?> GetByConfirmationTokenAsync(string token);
    Task MarkConfirmedAsync(Guid id, DateTime confirmedAt);
    Task<bool> HasPendingAsync(string bucket, string emailHash, string permission);
    Task CancelPendingAsync(string bucket, string emailHash, string permission);
    Task PruneExpiredAsync();
    Task<int> DeleteOldAsync(DateTime olderThan);
    Task<int> DeleteByEmailHashAsync(string emailHash, CancellationToken ct = default);
}
