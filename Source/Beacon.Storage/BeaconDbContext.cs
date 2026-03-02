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
    public DbSet<WebhookDeliveryError> WebhookDeliveryErrors => Set<WebhookDeliveryError>();
    public DbSet<SubmissionForm> SubmissionForms => Set<SubmissionForm>();
    public DbSet<ArchivedBucket> ArchivedBuckets => Set<ArchivedBucket>();
    public DbSet<BucketPermission> BucketPermissions => Set<BucketPermission>();
    public DbSet<SystemConfiguration> SystemConfigurations => Set<SystemConfiguration>();
    public DbSet<EmailQueueEntry> EmailQueueEntries => Set<EmailQueueEntry>();
    public DbSet<BucketOptions> BucketOptions => Set<BucketOptions>();
    public DbSet<User> Users => Set<User>();

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

            entity.Property(e => e.IpAddress)
                .HasMaxLength(45);

            entity.Property(e => e.ConsentText);

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

        modelBuilder.Entity<WebhookDeliveryError>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Bucket)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(e => e.ErrorMessage)
                .IsRequired();

            entity.Property(e => e.RequestUrl);
            entity.Property(e => e.RequestMethod).HasMaxLength(10);
            entity.Property(e => e.AttemptCount);
            entity.Property(e => e.StackTrace);

            entity.HasIndex(e => e.Bucket);
        });

        modelBuilder.Entity<ArchivedBucket>(entity =>
        {
            entity.HasKey(e => e.Bucket);

            entity.Property(e => e.Bucket)
                .HasMaxLength(100)
                .IsRequired();
        });

        modelBuilder.Entity<BucketPermission>(entity =>
        {
            entity.HasKey(e => new { e.Bucket, e.Permission });

            entity.Property(e => e.Bucket)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(e => e.Permission)
                .HasMaxLength(100)
                .IsRequired();
        });

        modelBuilder.Entity<SystemConfiguration>(entity =>
        {
            entity.ToTable("SystemConfiguration");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Configuration).IsRequired();
        });

        modelBuilder.Entity<EmailQueueEntry>(entity =>
        {
            entity.ToTable("EmailQueue"); // Match the table name created by DatabaseMigrator
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Bucket).HasMaxLength(100).IsRequired();
            entity.Property(e => e.EmailHash).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Permission).HasMaxLength(100).IsRequired();
            entity.Property(e => e.ConfirmationToken).HasMaxLength(64).IsRequired();
            entity.Property(e => e.ConfirmationUrl).IsRequired();
            entity.HasIndex(e => e.ConfirmationToken).IsUnique();
            entity.HasIndex(e => new { e.Bucket, e.EmailHash, e.Permission, e.Status });
            entity.HasIndex(e => e.Status);
        });

        modelBuilder.Entity<BucketOptions>(entity =>
        {
            entity.HasKey(e => e.Bucket);
            entity.Property(e => e.Bucket).HasMaxLength(100).IsRequired();
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Username)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(e => e.PasswordHash)
                .IsRequired();

            entity.Property(e => e.Salt)
                .IsRequired();

            entity.Property(e => e.Role)
                .HasMaxLength(20)
                .IsRequired();

            entity.Property(e => e.ApiKeyHash)
                .HasMaxLength(64)
                .IsRequired();

            entity.HasIndex(e => e.Username)
                .IsUnique();

            entity.HasIndex(e => e.ApiKeyHash)
                .IsUnique();
        });

        modelBuilder.Entity<SubmissionForm>(entity =>
        {
            entity.ToTable("NewsletterForms"); // Keep existing table name for backward compatibility
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Name)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(e => e.Bucket)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(e => e.Permission)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(e => e.AllowedOrigins)
                .IsRequired();

            entity.Property(e => e.EncryptedApiToken)
                .IsRequired();

            entity.Property(e => e.ConsentText)
                .HasMaxLength(500);

            entity.Property(e => e.PrivacyPolicyUrl)
                .HasMaxLength(2000);
        });
    }
}
