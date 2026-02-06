using Beacon.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Storage;

public class BeaconDbContext : DbContext
{
    public BeaconDbContext(DbContextOptions<BeaconDbContext> options) : base(options)
    {
    }

    public DbSet<ConsentRecord> ConsentRecords => Set<ConsentRecord>();
    public DbSet<UsedToken> UsedTokens => Set<UsedToken>();
    public DbSet<WebhookConfig> WebhookConfigs => Set<WebhookConfig>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ConsentRecord>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.EmailHash)
                .HasMaxLength(64)
                .IsRequired();

            entity.Property(e => e.Permission)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(e => e.TokenHash)
                .HasMaxLength(64);

            entity.HasIndex(e => new { e.Bucket, e.EmailHash, e.Permission })
                .IsUnique();

            entity.HasIndex(e => e.EmailHash);
        });

        modelBuilder.Entity<UsedToken>(entity =>
        {
            entity.HasKey(e => e.TokenHash);

            entity.Property(e => e.TokenHash)
                .HasMaxLength(64);

            entity.HasIndex(e => e.ExpiresAt);
        });

        modelBuilder.Entity<WebhookConfig>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Bucket)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(e => e.EncryptedUrl)
                .IsRequired();

            entity.Property(e => e.EncryptedMethod)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(e => e.EncryptedSecret);

            entity.HasIndex(e => e.Bucket)
                .IsUnique();
        });
    }
}
