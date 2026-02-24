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

    public async Task UpsertAsync(ConsentRecord record)
    {
        var existing = await _context.ConsentRecords
            .FirstOrDefaultAsync(r 
                => r.Bucket == record.Bucket 
                && r.EmailHash == record.EmailHash 
                && r.Permission == record.Permission);

        if (existing is null)
        {
            _context.ConsentRecords.Add(record);
        }
        else
        {
            // Update only mutable properties on the tracked entity
            existing.Status = record.Status;
            existing.Source = record.Source;
            existing.ChangedAt = record.ChangedAt;
            existing.TokenHash = record.TokenHash;
            existing.ExpiresAt = record.ExpiresAt;
            existing.EncryptedEmail ??= record.EncryptedEmail;
            if (record.CustomFields is not null)
            {
                existing.CustomFields = record.CustomFields;
            }
            // EF Core tracks changes automatically, no need for Update()
        }

        await _context.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<BucketInfo>> GetBucketsAsync()
    {
        // Load data first, then group in memory (SQLite doesn't support APPLY)
        var records = await _context.ConsentRecords.ToListAsync();

        return records
            .GroupBy(r => r.Bucket)
            .Select(g => new BucketInfo
            {
                Name = g.Key,
                TotalEmails = g.Select(r => r.EmailHash).Distinct().Count(),
                Permissions = g.Select(r => r.Permission).Distinct().OrderBy(p => p).ToList()
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
        // Get all records for this bucket
        var bucketRecords = await _context.ConsentRecords
            .Where(r => r.Bucket == bucket)
            .ToListAsync();

        // Group by email hash and build permission dictionary
        var emailGroups = bucketRecords
            .GroupBy(r => r.EmailHash)
            .Select(g => new EmailPermissions
            {
                EmailHash = g.Key,
                EncryptedEmail = g.FirstOrDefault(r => r.EncryptedEmail != null)?.EncryptedEmail,
                Permissions = g.ToDictionary(r => r.Permission, r => r.Status == ConsentStatus.OptedIn),
                LastChanged = g.Max(r => r.ChangedAt),
                CustomFields = DeserializeCustomFields(g.FirstOrDefault(r => r.CustomFields != null)?.CustomFields)
            })
            .ToList();

        // Filter by search (matches emailHash prefix which is the ID shown in logs)
        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchLower = search.ToLowerInvariant();
            emailGroups = emailGroups
                .Where(e => e.EmailHash.StartsWith(searchLower, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        // Apply sorting
        IEnumerable<EmailPermissions> sorted = sortBy?.ToLowerInvariant() switch
        {
            "email" => sortDir == "asc"
                ? emailGroups.OrderBy(e => e.EmailHash)
                : emailGroups.OrderByDescending(e => e.EmailHash),
            "lastchanged" => sortDir == "asc"
                ? emailGroups.OrderBy(e => e.LastChanged)
                : emailGroups.OrderByDescending(e => e.LastChanged),
            _ => emailGroups.OrderByDescending(e => e.LastChanged) // Default sort
        };

        var sortedList = sorted.ToList();
        var total = sortedList.Count;
        var pagedRecords = sortedList
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
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

    public async Task<int> DeleteBucketAsync(string bucket)
    {
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

        // Count distinct identities for pagination total — separate query so EF
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

    public async Task<IReadOnlyList<(string EmailHash, string? EncryptedEmail)>> GetEmailHashMappingsAsync()
    {
        var rows = await _context.ConsentRecords
            .GroupBy(r => r.EmailHash)
            .Select(g => new
            {
                EmailHash = g.Key,
                EncryptedEmail = g.Where(r => r.EncryptedEmail != null).Select(r => r.EncryptedEmail).FirstOrDefault()
            })
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
