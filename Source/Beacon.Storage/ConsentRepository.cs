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
            .FirstOrDefaultAsync(r => r.Bucket == record.Bucket && r.EmailHash == record.EmailHash && r.Permission == record.Permission);

        if (existing is null)
        {
            _context.ConsentRecords.Add(record);
        }
        else
        {
            existing.Status = record.Status;
            existing.Source = record.Source;
            existing.ChangedAt = record.ChangedAt;
            existing.TokenHash = record.TokenHash;
            existing.ExpiresAt = record.ExpiresAt;
            existing.EncryptedEmail ??= record.EncryptedEmail;  // Update if not already set
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
                LastChanged = g.Max(r => r.ChangedAt)
            })
            .ToList();

        // Filter by search (matches emailHash prefix - the ID shown in logs)
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
                LastChanged = g.Max(r => r.ChangedAt)
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
}
