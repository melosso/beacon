using System.Text.Json;
using Beacon.Core.Models;
using Beacon.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Storage;

public sealed class ConsentRepository : IConsentRepository
{
    private readonly BeaconDbContext _context;

    public ConsentRepository(BeaconDbContext context)
    {
        _context = context;
    }

    public async Task<ConsentRecord?> GetAsync(string bucket, string emailHash, string permission)
    {
        return await _context.ConsentRecords
            .FirstOrDefaultAsync(r => r.Bucket == bucket && r.EmailHash == emailHash && r.Permission == permission);
    }

    public async Task UpsertAsync(ConsentRecord record, string? actorId = null)
    {
        var existing = await _context.ConsentRecords
            .FirstOrDefaultAsync(r
                => r.Bucket == record.Bucket
                && r.EmailHash == record.EmailHash
                && r.Permission == record.Permission);

        ConsentStatus? oldStatus = existing?.Status;

        if (existing is null)
        {
            _context.ConsentRecords.Add(record);
        }
        else
        {
            ApplyUpdate(existing, record);
        }

        if (oldStatus != record.Status)
            _context.ConsentAuditEntries.Add(BuildAuditEntry(record, oldStatus, actorId));

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            // Lost the concurrent-insert race: reload and apply as update.
            _context.ChangeTracker.Clear();

            var winner = await _context.ConsentRecords
                .FirstOrDefaultAsync(r
                    => r.Bucket == record.Bucket
                    && r.EmailHash == record.EmailHash
                    && r.Permission == record.Permission);

            if (winner is null) throw;

            var prevStatus = winner.Status;
            ApplyUpdate(winner, record);

            if (prevStatus != record.Status)
                _context.ConsentAuditEntries.Add(BuildAuditEntry(record, prevStatus, actorId));

            await _context.SaveChangesAsync();
        }
    }

    private static void ApplyUpdate(ConsentRecord target, ConsentRecord src)
    {
        target.Status = src.Status;
        target.Source = src.Source;
        target.ChangedAt = src.ChangedAt;
        target.TokenHash = src.TokenHash;
        target.ExpiresAt = src.ExpiresAt;
        target.EncryptedEmail ??= src.EncryptedEmail;
        if (src.CustomFields is not null)
            target.CustomFields = src.CustomFields;
    }

    private static ConsentAuditEntry BuildAuditEntry(ConsentRecord record, ConsentStatus? oldStatus, string? actorId) =>
        new()
        {
            Id = Guid.NewGuid(),
            Bucket = record.Bucket,
            EmailHash = record.EmailHash,
            Permission = record.Permission,
            OldStatus = oldStatus,
            NewStatus = record.Status,
            Source = record.Source,
            ActorId = actorId,
            ChangedAt = record.ChangedAt,
            IpAddress = record.IpAddress,
            CustomFields = record.CustomFields
        };

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        var msg = ex.InnerException?.Message;
        if (msg is null) return false;
        return msg.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase)  // SQLite
            || msg.Contains("duplicate key", StringComparison.OrdinalIgnoreCase)              // SQL Server
            || msg.Contains("Duplicate entry", StringComparison.OrdinalIgnoreCase)            // MySQL
            || msg.Contains("unique constraint", StringComparison.OrdinalIgnoreCase);         // PostgreSQL
    }

    public async Task<PagedResult<ConsentAuditEntry>> GetAuditAsync(
        string? bucket, string? emailHash, int page, int pageSize, CancellationToken ct = default)
    {
        var query = _context.ConsentAuditEntries.AsQueryable();
        if (!string.IsNullOrWhiteSpace(bucket))
            query = query.Where(e => e.Bucket == bucket);
        if (!string.IsNullOrWhiteSpace(emailHash))
            query = query.Where(e => e.EmailHash.StartsWith(emailHash));

        var total = await query.CountAsync(ct);
        var records = await query
            .OrderByDescending(e => e.ChangedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<ConsentAuditEntry>
        {
            Records = records,
            Total = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<IReadOnlyList<BucketInfo>> GetBucketsAsync()
    {
        var summaries = await _context.ConsentRecords
            .GroupBy(r => r.Bucket)
            .Select(g => new { Name = g.Key, TotalEmails = g.Select(r => r.EmailHash).Distinct().Count() })
            .ToListAsync();

        var permRows = await _context.ConsentRecords
            .Select(r => new { r.Bucket, r.Permission })
            .Distinct()
            .ToListAsync();

        var permsByBucket = permRows
            .GroupBy(x => x.Bucket)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(x => x.Permission).OrderBy(p => p).ToList());

        return summaries
            .Select(s => new BucketInfo
            {
                Name = s.Name,
                TotalEmails = s.TotalEmails,
                Permissions = permsByBucket.TryGetValue(s.Name, out var perms) ? perms : []
            })
            .OrderBy(b => b.Name)
            .ToList();
    }

    public async Task<BucketDetails> GetBucketDetailsAsync(string bucket)
    {
        var records = await _context.ConsentRecords
            .Where(r => r.Bucket == bucket)
            .ToListAsync();

        var permissions = records.Select(r => r.Permission).Distinct().OrderBy(p => p).ToList();
        var stats = permissions.Select(p => new PermissionStats
        {
            Permission = p,
            OptedIn = records.Count(r => r.Permission == p && r.Status == ConsentStatus.OptedIn),
            OptedOut = records.Count(r => r.Permission == p && r.Status == ConsentStatus.OptedOut)
        }).ToList();

        return new BucketDetails
        {
            Name = bucket,
            Permissions = permissions,
            Stats = stats
        };
    }

    public async Task<PagedResult<EmailPermissions>> GetBucketRecordsAsync(string bucket, int page, int pageSize, string? sortBy = null, string? sortDir = null, string? search = null)
    {
        var baseQuery = _context.ConsentRecords.Where(r => r.Bucket == bucket);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchLower = search.ToLowerInvariant();
            baseQuery = baseQuery.Where(r => r.EmailHash.StartsWith(searchLower));
        }

        // Group at DB level to get one row per identity with a sort key.
        var grouped = baseQuery
            .GroupBy(r => r.EmailHash)
            .Select(g => new { EmailHash = g.Key, LastChanged = g.Max(r => r.ChangedAt) });

        var total = await grouped.CountAsync();

        var ordered = sortBy?.ToLowerInvariant() switch
        {
            "email" => sortDir == "asc"
                ? grouped.OrderBy(g => g.EmailHash)
                : grouped.OrderByDescending(g => g.EmailHash),
            "lastchanged" => sortDir == "asc"
                ? grouped.OrderBy(g => g.LastChanged)
                : grouped.OrderByDescending(g => g.LastChanged),
            _ => grouped.OrderByDescending(g => g.LastChanged)
        };

        var hashPage = await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        if (hashPage.Count == 0)
            return new PagedResult<EmailPermissions> { Records = [], Total = total, Page = page, PageSize = pageSize };

        var emailHashes = hashPage.Select(h => h.EmailHash).ToList();

        // Load records for this page only.
        var pageRecords = await _context.ConsentRecords
            .Where(r => r.Bucket == bucket && emailHashes.Contains(r.EmailHash))
            .ToListAsync();

        var emailGroups = pageRecords
            .GroupBy(r => r.EmailHash)
            .ToDictionary(g => g.Key, g => new EmailPermissions
            {
                EmailHash = g.Key,
                EncryptedEmail = g.FirstOrDefault(r => r.EncryptedEmail != null)?.EncryptedEmail,
                Permissions = g.ToDictionary(r => r.Permission, r => r.Status == ConsentStatus.OptedIn),
                LastChanged = g.Max(r => r.ChangedAt),
                CustomFields = DeserializeCustomFields(g.FirstOrDefault(r => r.CustomFields != null)?.CustomFields)
            });

        // Preserve DB sort order.
        var pagedRecords = hashPage
            .Where(h => emailGroups.ContainsKey(h.EmailHash))
            .Select(h => emailGroups[h.EmailHash])
            .ToList();

        return new PagedResult<EmailPermissions>
        {
            Records = pagedRecords,
            Total = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<IReadOnlyList<EmailPermissions>> GetAllBucketRecordsAsync(string bucket)
    {
        var bucketRecords = await _context.ConsentRecords
            .Where(r => r.Bucket == bucket)
            .ToListAsync();

        return bucketRecords
            .GroupBy(r => r.EmailHash)
            .Select(g => new EmailPermissions
            {
                EmailHash = g.Key,
                EncryptedEmail = g.FirstOrDefault(r => r.EncryptedEmail != null)?.EncryptedEmail,
                Permissions = g.ToDictionary(r => r.Permission, r => r.Status == ConsentStatus.OptedIn),
                LastChanged = g.Max(r => r.ChangedAt),
                CustomFields = DeserializeCustomFields(g.FirstOrDefault(r => r.CustomFields != null)?.CustomFields)
            })
            .OrderByDescending(e => e.LastChanged)
            .ToList();
    }

    public async Task DeleteAuditEntriesByEmailHashAsync(string emailHash, string bucket, CancellationToken ct = default)
    {
        await _context.ConsentAuditEntries
            .Where(e => e.EmailHash == emailHash && e.Bucket == bucket)
            .ExecuteDeleteAsync(ct);
    }

    public async Task<int> DeleteBucketAsync(string bucket)
    {
        await _context.ConsentAuditEntries
            .Where(e => e.Bucket == bucket)
            .ExecuteDeleteAsync();

        var records = await _context.ConsentRecords
            .Where(r => r.Bucket == bucket)
            .ToListAsync();

        _context.ConsentRecords.RemoveRange(records);
        await _context.SaveChangesAsync();

        return records.Count;
    }

    public async Task<int> DeletePermissionAsync(string bucket, string permission)
    {
        var records = await _context.ConsentRecords
            .Where(r => r.Bucket == bucket && r.Permission == permission)
            .ToListAsync();

        _context.ConsentRecords.RemoveRange(records);
        await _context.SaveChangesAsync();

        return records.Count;
    }

    public async Task<int> DeleteRecordAsync(string bucket, string emailHash)
    {
        await _context.ConsentAuditEntries
            .Where(e => e.EmailHash == emailHash && e.Bucket == bucket)
            .ExecuteDeleteAsync();

        var records = await _context.ConsentRecords
            .Where(r => r.Bucket == bucket && r.EmailHash == emailHash)
            .ToListAsync();

        _context.ConsentRecords.RemoveRange(records);
        await _context.SaveChangesAsync();

        return records.Count;
    }

    public async Task<bool> EmailExistsInBucketAsync(string bucket, string emailHash)
    {
        return await _context.ConsentRecords
            .AnyAsync(r => r.Bucket == bucket && r.EmailHash == emailHash);
    }

    public async Task<IReadOnlyList<ConsentRecord>> GetByEmailAsync(string bucket, string emailHash)
    {
        return await _context.ConsentRecords
            .Where(r => r.Bucket == bucket && r.EmailHash == emailHash)
            .ToListAsync();
    }

    public async Task<PagedResult<IdentityInfo>> GetIdentitiesAsync(int page, int pageSize, string? sortBy = null, string? sortDir = null, string? search = null)
    {
        var baseQuery = _context.ConsentRecords.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchLower = search.ToLowerInvariant();
            baseQuery = baseQuery.Where(r => r.EmailHash.StartsWith(searchLower));
        }

        // Count distinct identities for pagination total, separate query so EF
        // doesn't need to wrap the grouped projection in a subquery count.
        var total = await baseQuery
            .Select(r => r.EmailHash)
            .Distinct()
            .CountAsync();

        var grouped = baseQuery
            .GroupBy(r => r.EmailHash)
            .Select(g => new IdentityInfo
            {
                EmailHash = g.Key,
                BucketCount = g.Select(r => r.Bucket).Distinct().Count(),
                FirstSeen = g.Min(r => r.ChangedAt),
                LastChanged = g.Max(r => r.ChangedAt)
            });

        IQueryable<IdentityInfo> sorted = sortBy?.ToLowerInvariant() switch
        {
            "id" => sortDir == "asc"
                ? grouped.OrderBy(i => i.EmailHash)
                : grouped.OrderByDescending(i => i.EmailHash),
            "buckets" => sortDir == "asc"
                ? grouped.OrderBy(i => i.BucketCount)
                : grouped.OrderByDescending(i => i.BucketCount),
            "firstseen" => sortDir == "asc"
                ? grouped.OrderBy(i => i.FirstSeen)
                : grouped.OrderByDescending(i => i.FirstSeen),
            _ => sortDir == "asc"
                ? grouped.OrderBy(i => i.LastChanged)
                : grouped.OrderByDescending(i => i.LastChanged)
        };

        var paged = await sorted
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PagedResult<IdentityInfo>
        {
            Records = paged,
            Total = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<Dictionary<string, string?>> GetEncryptedEmailsForHashesAsync(
        IReadOnlyList<string> hashes, CancellationToken ct = default)
    {
        var rows = await _context.ConsentRecords
            .Where(r => hashes.Contains(r.EmailHash))
            .GroupBy(r => r.EmailHash)
            .Select(g => new
            {
                EmailHash = g.Key,
                EncryptedEmail = g.Where(r => r.EncryptedEmail != null).Select(r => r.EncryptedEmail).FirstOrDefault()
            })
            .ToListAsync(ct);

        return rows.ToDictionary(r => r.EmailHash, r => r.EncryptedEmail);
    }

    public async Task<IReadOnlyList<(string EmailHash, string? EncryptedEmail)>> GetEmailHashMappingsAsync()
    {
        // Capped: encrypted values cannot be searched in SQL, so we decrypt in memory.
        // 10k is a pragmatic upper bound to prevent unbounded work.
        var rows = await _context.ConsentRecords
            .GroupBy(r => r.EmailHash)
            .Select(g => new
            {
                EmailHash = g.Key,
                EncryptedEmail = g.Where(r => r.EncryptedEmail != null).Select(r => r.EncryptedEmail).FirstOrDefault()
            })
            .Take(10_000)
            .ToListAsync();

        return rows.Select(r => (r.EmailHash, r.EncryptedEmail)).ToList();
    }

    public async Task<PagedResult<IdentityInfo>> GetIdentitiesByHashesAsync(
        IReadOnlyList<string> hashes, int page, int pageSize,
        string? sortBy = null, string? sortDir = null)
    {
        if (hashes.Count == 0)
            return new PagedResult<IdentityInfo> { Records = [], Total = 0, Page = page, PageSize = pageSize };

        var baseQuery = _context.ConsentRecords
            .Where(r => hashes.Contains(r.EmailHash));

        var total = await baseQuery
            .Select(r => r.EmailHash)
            .Distinct()
            .CountAsync();

        var grouped = baseQuery
            .GroupBy(r => r.EmailHash)
            .Select(g => new IdentityInfo
            {
                EmailHash = g.Key,
                BucketCount = g.Select(r => r.Bucket).Distinct().Count(),
                FirstSeen = g.Min(r => r.ChangedAt),
                LastChanged = g.Max(r => r.ChangedAt)
            });

        IQueryable<IdentityInfo> sorted = sortBy?.ToLowerInvariant() switch
        {
            "id" => sortDir == "asc"
                ? grouped.OrderBy(i => i.EmailHash)
                : grouped.OrderByDescending(i => i.EmailHash),
            "buckets" => sortDir == "asc"
                ? grouped.OrderBy(i => i.BucketCount)
                : grouped.OrderByDescending(i => i.BucketCount),
            "firstseen" => sortDir == "asc"
                ? grouped.OrderBy(i => i.FirstSeen)
                : grouped.OrderByDescending(i => i.FirstSeen),
            _ => sortDir == "asc"
                ? grouped.OrderBy(i => i.LastChanged)
                : grouped.OrderByDescending(i => i.LastChanged)
        };

        var paged = await sorted
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PagedResult<IdentityInfo>
        {
            Records = paged,
            Total = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<IdentityDetails?> GetIdentityDetailsAsync(string emailHash)
    {
        var subscriptions = await GetSubscriptionsAsync(emailHash);
        if (subscriptions.Count == 0) return null;

        var encryptedEmail = await _context.ConsentRecords
            .Where(r => r.EmailHash == emailHash && r.EncryptedEmail != null)
            .Select(r => r.EncryptedEmail)
            .FirstOrDefaultAsync();

        return new IdentityDetails
        {
            EmailHash = emailHash,
            EncryptedEmail = encryptedEmail,
            Subscriptions = subscriptions
        };
    }

    private async Task<IReadOnlyList<BucketSubscription>> GetSubscriptionsAsync(string emailHash)
    {
        var records = await _context.ConsentRecords
            .Where(r => r.EmailHash == emailHash)
            .ToListAsync();

        return records
            .GroupBy(r => r.Bucket)
            .Select(g => new BucketSubscription
            {
                Bucket = g.Key,
                Permissions = g.ToDictionary(r => r.Permission, r => r.Status == ConsentStatus.OptedIn),
                LastChanged = g.Max(r => r.ChangedAt)
            })
            .OrderBy(b => b.Bucket)
            .ToList();
    }

    public async Task<IDisposable> BeginTransactionAsync()
    {
        return await _context.Database.BeginTransactionAsync();
    }

    public async Task CommitTransactionAsync()
    {
        if (_context.Database.CurrentTransaction != null)
        {
            await _context.Database.CurrentTransaction.CommitAsync();
        }
    }

    public async Task<int> AnonymiseOptedOutAsync(DateTime cutoff, CancellationToken ct = default)
    {
        return await _context.ConsentRecords
            .Where(r => r.Status == ConsentStatus.OptedOut
                     && r.ChangedAt < cutoff
                     && r.EncryptedEmail != null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.EncryptedEmail, (string?)null)
                .SetProperty(r => r.IpAddress, (string?)null)
                .SetProperty(r => r.CustomFields, (string?)null)
                .SetProperty(r => r.ConsentText, (string?)null), ct);
    }

    public async Task<int> PurgePendingConfirmationAsync(DateTime cutoff, CancellationToken ct = default)
    {
        return await _context.ConsentRecords
            .Where(r => r.Status == ConsentStatus.PendingConfirmation && r.ChangedAt < cutoff)
            .ExecuteDeleteAsync(ct);
    }

    public async Task<int> CountOptedOutToAnonymiseAsync(DateTime cutoff, CancellationToken ct = default)
    {
        return await _context.ConsentRecords
            .CountAsync(r => r.Status == ConsentStatus.OptedOut
                          && r.ChangedAt < cutoff
                          && r.EncryptedEmail != null, ct);
    }

    public async Task<int> CountPendingConfirmationToPurgeAsync(DateTime cutoff, CancellationToken ct = default)
    {
        return await _context.ConsentRecords
            .CountAsync(r => r.Status == ConsentStatus.PendingConfirmation && r.ChangedAt < cutoff, ct);
    }

    private static Dictionary<string, string>? DeserializeCustomFields(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        }
        catch
        {
            return null;
        }
    }
}
