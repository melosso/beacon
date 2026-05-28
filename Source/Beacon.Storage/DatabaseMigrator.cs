using Microsoft.EntityFrameworkCore;

namespace Beacon.Storage;

public static class DatabaseMigrator
{
    public static void Initialize(BeaconDbContext db)
    {
        db.Database.EnsureCreated();

        MigrateConsentRecords(db);
        MigrateWebhookConfigs(db);
        MigrateWebhookDeliveryErrors(db);
        MigrateSubmissionForms(db);
        MigrateArchivedBuckets(db);
        MigrateBucketPermissions(db);
        MigrateSystemConfiguration(db);
        MigrateEmailQueue(db);
        MigrateBucketOptions(db);
        MigrateUsers(db);
        MigrateWorkflowTasks(db);
        MigrateConsentAuditEntries(db);
        MigrateApiKeys(db);
        MigrateBrandIdentities(db);
    }

    private static HashSet<string> GetColumns(BeaconDbContext db, string table)
    {
        return db.Database.SqlQueryRaw<string>(
                string.Concat("SELECT name AS Value FROM pragma_table_info('", table, "')"))
            .AsEnumerable().ToHashSet();
    }

    private static bool TableExists(BeaconDbContext db, string table)
    {
        return db.Database.SqlQueryRaw<int>(
                string.Concat("SELECT COUNT(*) AS Value FROM sqlite_master WHERE type='table' AND name='", table, "'"))
            .AsEnumerable().FirstOrDefault() > 0;
    }

    private static void AddColumnIfMissing(BeaconDbContext db, string table, string column, string type)
    {
        var columns = GetColumns(db, table);
        if (!columns.Contains(column))
            db.Database.ExecuteSqlRaw(string.Concat("ALTER TABLE ", table, " ADD COLUMN ", column, " ", type));
    }

    private static void MigrateConsentRecords(BeaconDbContext db)
    {
        AddColumnIfMissing(db, "ConsentRecords", "CustomFields", "TEXT NULL");
        AddColumnIfMissing(db, "ConsentRecords", "IpAddress", "TEXT NULL");
        AddColumnIfMissing(db, "ConsentRecords", "ConsentText", "TEXT NULL");
        AddColumnIfMissing(db, "ConsentRecords", "EncryptedName", "TEXT NULL");
    }

    private static void MigrateWebhookConfigs(BeaconDbContext db)
    {
        if (!TableExists(db, "WebhookConfigs"))
        {
            db.Database.ExecuteSqlRaw("""
                CREATE TABLE WebhookConfigs (
                    Id TEXT NOT NULL PRIMARY KEY,
                    Bucket TEXT NOT NULL,
                    EncryptedUrl TEXT NOT NULL,
                    EncryptedMethod TEXT NOT NULL,
                    EncryptedHeaders TEXT NULL,
                    EncryptedSecret TEXT NULL,
                    BodyTemplate TEXT NULL,
                    IsEnabled INTEGER NOT NULL DEFAULT 1,
                    CreatedAt TEXT NOT NULL,
                    LastTriggeredAt TEXT NULL,
                    TriggerCount INTEGER NOT NULL DEFAULT 0
                )
                """);
            db.Database.ExecuteSqlRaw("CREATE UNIQUE INDEX IX_WebhookConfigs_Bucket ON WebhookConfigs (Bucket)");
        }
        else
        {
            AddColumnIfMissing(db, "WebhookConfigs", "EncryptedSecret", "TEXT NULL");
        }
    }

    private static void MigrateWebhookDeliveryErrors(BeaconDbContext db)
    {
        if (!TableExists(db, "WebhookDeliveryErrors"))
        {
            db.Database.ExecuteSqlRaw("""
                CREATE TABLE WebhookDeliveryErrors (
                    Id TEXT NOT NULL PRIMARY KEY,
                    Bucket TEXT NOT NULL,
                    ErrorMessage TEXT NOT NULL,
                    StatusCode INTEGER NOT NULL DEFAULT 0,
                    RequestUrl TEXT,
                    RequestMethod TEXT,
                    AttemptCount INTEGER NOT NULL DEFAULT 0,
                    StackTrace TEXT,
                    OccurredAt TEXT NOT NULL
                )
                """);
            db.Database.ExecuteSqlRaw("CREATE INDEX IX_WebhookDeliveryErrors_Bucket ON WebhookDeliveryErrors (Bucket)");
        }
        else
        {
            AddColumnIfMissing(db, "WebhookDeliveryErrors", "RequestUrl", "TEXT");
            AddColumnIfMissing(db, "WebhookDeliveryErrors", "RequestMethod", "TEXT");
            AddColumnIfMissing(db, "WebhookDeliveryErrors", "AttemptCount", "INTEGER NOT NULL DEFAULT 0");
            AddColumnIfMissing(db, "WebhookDeliveryErrors", "StackTrace", "TEXT");
        }
    }

    private static void MigrateArchivedBuckets(BeaconDbContext db)
    {
        if (!TableExists(db, "ArchivedBuckets"))
        {
            db.Database.ExecuteSqlRaw("""
                CREATE TABLE ArchivedBuckets (
                    Bucket TEXT NOT NULL PRIMARY KEY,
                    ArchivedAt TEXT NOT NULL
                )
                """);
        }
    }

    private static void MigrateBucketPermissions(BeaconDbContext db)
    {
        if (!TableExists(db, "BucketPermissions"))
        {
            db.Database.ExecuteSqlRaw("""
                CREATE TABLE BucketPermissions (
                    Bucket TEXT NOT NULL,
                    Permission TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    PRIMARY KEY (Bucket, Permission)
                )
                """);
        }
    }

    private static void MigrateSystemConfiguration(BeaconDbContext db)
    {
        if (!TableExists(db, "SystemConfiguration"))
        {
            db.Database.ExecuteSqlRaw("""
                CREATE TABLE SystemConfiguration (
                    Id INTEGER NOT NULL PRIMARY KEY,
                    Configuration TEXT NOT NULL DEFAULT '{{}}',
                    UpdatedAt TEXT NOT NULL
                )
                """);
        }
    }

    private static void MigrateEmailQueue(BeaconDbContext db)
    {
        if (!TableExists(db, "EmailQueue"))
        {
            db.Database.ExecuteSqlRaw("""
                CREATE TABLE EmailQueue (
                    Id TEXT NOT NULL PRIMARY KEY,
                    Bucket TEXT NOT NULL,
                    EncryptedEmail TEXT NOT NULL,
                    EmailHash TEXT NOT NULL,
                    Permission TEXT NOT NULL,
                    Language TEXT NOT NULL DEFAULT 'en',
                    ConfirmationToken TEXT NOT NULL UNIQUE,
                    ConfirmationUrl TEXT NOT NULL,
                    Status INTEGER NOT NULL DEFAULT 0,
                    CreatedAt TEXT NOT NULL,
                    SentAt TEXT NULL,
                    ConfirmedAt TEXT NULL,
                    ExpiresAt TEXT NOT NULL,
                    AttemptCount INTEGER NOT NULL DEFAULT 0,
                    LastError TEXT NULL,
                    NextAttemptAt TEXT NULL
                )
                """);
            db.Database.ExecuteSqlRaw("CREATE INDEX IX_EmailQueue_Status ON EmailQueue (Status)");
            db.Database.ExecuteSqlRaw("CREATE INDEX IX_EmailQueue_Lookup ON EmailQueue (Bucket, EmailHash, Permission, Status)");
        }
    }

    private static void MigrateBucketOptions(BeaconDbContext db)
    {
        if (!TableExists(db, "BucketOptions"))
        {
            db.Database.ExecuteSqlRaw("""
                CREATE TABLE BucketOptions (
                    Bucket TEXT NOT NULL PRIMARY KEY,
                    DoubleOptIn INTEGER NOT NULL DEFAULT 1,
                    UpdatedAt TEXT NULL
                )
                """);
        }

        AddColumnIfMissing(db, "BucketOptions", "UtmCampaign", "TEXT NULL");
    }

    private static void MigrateUsers(BeaconDbContext db)
    {
        if (!TableExists(db, "Users"))
        {
            db.Database.ExecuteSqlRaw("""
                CREATE TABLE Users (
                    Id TEXT NOT NULL PRIMARY KEY,
                    Username TEXT NOT NULL,
                    PasswordHash TEXT NOT NULL,
                    Salt TEXT NOT NULL,
                    Role TEXT NOT NULL DEFAULT 'admin',
                    ApiKeyHash TEXT NOT NULL,
                    IsEnabled INTEGER NOT NULL DEFAULT 1,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NULL,
                    LastLoginAt TEXT NULL
                )
                """);
            db.Database.ExecuteSqlRaw("CREATE UNIQUE INDEX IX_Users_Username ON Users (Username COLLATE NOCASE)");
            db.Database.ExecuteSqlRaw("CREATE UNIQUE INDEX IX_Users_ApiKeyHash ON Users (ApiKeyHash)");
        }
    }

    private static void MigrateWorkflowTasks(BeaconDbContext db)
    {
        if (!TableExists(db, "WorkflowTasks"))
        {
            db.Database.ExecuteSqlRaw("""
                CREATE TABLE WorkflowTasks (
                    Id TEXT NOT NULL PRIMARY KEY,
                    TaskType TEXT NOT NULL,
                    Status TEXT NOT NULL DEFAULT 'Pending',
                    TriggeredBy TEXT NOT NULL DEFAULT 'cron',
                    ScheduledAt TEXT NOT NULL,
                    StartedAt TEXT NULL,
                    CompletedAt TEXT NULL,
                    RecordsAffected INTEGER NOT NULL DEFAULT 0,
                    Notes TEXT NULL,
                    ErrorMessage TEXT NULL
                )
                """);
            db.Database.ExecuteSqlRaw("CREATE INDEX IX_WorkflowTasks_ScheduledAt ON WorkflowTasks (ScheduledAt)");
        }
    }

    private static void MigrateConsentAuditEntries(BeaconDbContext db)
    {
        if (!TableExists(db, "ConsentAuditEntries"))
        {
            db.Database.ExecuteSqlRaw("""
                CREATE TABLE ConsentAuditEntries (
                    Id TEXT NOT NULL PRIMARY KEY,
                    Bucket TEXT NOT NULL,
                    EmailHash TEXT NOT NULL,
                    Permission TEXT NOT NULL,
                    OldStatus INTEGER NULL,
                    NewStatus INTEGER NOT NULL,
                    Source INTEGER NOT NULL,
                    ActorId TEXT NULL,
                    ChangedAt TEXT NOT NULL,
                    IpAddress TEXT NULL
                )
                """);
            db.Database.ExecuteSqlRaw(
                "CREATE INDEX IX_ConsentAuditEntries_EmailHash ON ConsentAuditEntries (EmailHash)");
            db.Database.ExecuteSqlRaw(
                "CREATE INDEX IX_ConsentAuditEntries_ChangedAt ON ConsentAuditEntries (ChangedAt)");
            db.Database.ExecuteSqlRaw(
                "CREATE INDEX IX_ConsentAuditEntries_Bucket_ChangedAt ON ConsentAuditEntries (Bucket, ChangedAt)");
        }

        AddColumnIfMissing(db, "ConsentAuditEntries", "CustomFields", "TEXT NULL");
    }

    private static void MigrateSubmissionForms(BeaconDbContext db)
    {
        if (!TableExists(db, "NewsletterForms"))
        {
            db.Database.ExecuteSqlRaw("""
                CREATE TABLE NewsletterForms (
                    Id TEXT NOT NULL PRIMARY KEY,
                    Name TEXT NOT NULL,
                    Bucket TEXT NOT NULL,
                    Permission TEXT NOT NULL,
                    AllowedOrigins TEXT NOT NULL,
                    FormConfig TEXT NULL,
                    EncryptedApiToken TEXT NOT NULL,
                    RateLimitPerMinute INTEGER NOT NULL DEFAULT 10,
                    HoneypotEnabled INTEGER NOT NULL DEFAULT 1,
                    DoubleOptIn INTEGER NOT NULL DEFAULT 0,
                    Language TEXT NOT NULL DEFAULT 'en',
                    IsEnabled INTEGER NOT NULL DEFAULT 1,
                    RedirectSuccess TEXT NULL,
                    RedirectError TEXT NULL,
                    RedirectFormPost INTEGER NOT NULL DEFAULT 1,
                    RedirectJsEmbed INTEGER NOT NULL DEFAULT 0,
                    DisableRedirects INTEGER NOT NULL DEFAULT 0,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NULL,
                    SubmissionCount INTEGER NOT NULL DEFAULT 0,
                    ConsentRequired INTEGER NOT NULL DEFAULT 1,
                    ConsentText TEXT NULL,
                    PrivacyPolicyUrl TEXT NULL,
                    CustomFields TEXT NULL
                )
                """);
        }
        else
        {
            AddColumnIfMissing(db, "NewsletterForms", "Language", "TEXT NOT NULL DEFAULT 'en'");
            AddColumnIfMissing(db, "NewsletterForms", "RedirectSuccess", "TEXT NULL");
            AddColumnIfMissing(db, "NewsletterForms", "RedirectError", "TEXT NULL");
            AddColumnIfMissing(db, "NewsletterForms", "ConsentRequired", "INTEGER NOT NULL DEFAULT 1");
            AddColumnIfMissing(db, "NewsletterForms", "ConsentText", "TEXT NULL");
            AddColumnIfMissing(db, "NewsletterForms", "PrivacyPolicyUrl", "TEXT NULL");
            AddColumnIfMissing(db, "NewsletterForms", "CustomFields", "TEXT NULL");
            AddColumnIfMissing(db, "NewsletterForms", "RedirectJsEmbed", "INTEGER NOT NULL DEFAULT 0");
            AddColumnIfMissing(db, "NewsletterForms", "RedirectFormPost", "INTEGER NOT NULL DEFAULT 1");
            AddColumnIfMissing(db, "NewsletterForms", "DisableRedirects", "INTEGER NOT NULL DEFAULT 0");
            AddColumnIfMissing(db, "NewsletterForms", "CollectName", "INTEGER NOT NULL DEFAULT 0");
            AddColumnIfMissing(db, "NewsletterForms", "NameLabel", "TEXT NULL");
        }
    }

    private static void MigrateApiKeys(BeaconDbContext db)
    {
        if (!TableExists(db, "ApiKeys"))
        {
            db.Database.ExecuteSqlRaw("""
                CREATE TABLE ApiKeys (
                    Id TEXT NOT NULL PRIMARY KEY,
                    Name TEXT NOT NULL,
                    KeyHash TEXT NOT NULL,
                    Permissions TEXT NOT NULL DEFAULT '[]',
                    IsEnabled INTEGER NOT NULL DEFAULT 1,
                    ActiveFrom TEXT NULL,
                    ActiveUntil TEXT NULL,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NULL,
                    LastUsedAt TEXT NULL
                )
                """);
            db.Database.ExecuteSqlRaw("CREATE UNIQUE INDEX IX_ApiKeys_KeyHash ON ApiKeys (KeyHash)");
        }
    }

    private static void MigrateBrandIdentities(BeaconDbContext db)
    {
        if (!TableExists(db, "BrandIdentities"))
        {
            db.Database.ExecuteSqlRaw("""
                CREATE TABLE BrandIdentities (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    Settings TEXT NOT NULL DEFAULT '{{}}',
                    IsDefault INTEGER NOT NULL DEFAULT 0,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL
                )
                """);
            db.Database.ExecuteSqlRaw("""
                INSERT INTO BrandIdentities (Id, Name, Settings, IsDefault, CreatedAt, UpdatedAt)
                VALUES (1, 'Default', '{{}}', 1, datetime('now'), datetime('now'))
                """);
        }

        if (!TableExists(db, "BucketIdentities"))
        {
            db.Database.ExecuteSqlRaw("""
                CREATE TABLE BucketIdentities (
                    Bucket TEXT NOT NULL PRIMARY KEY,
                    BrandIdentityId INTEGER NOT NULL REFERENCES BrandIdentities(Id) ON DELETE CASCADE
                )
                """);
        }

        db.Database.ExecuteSqlRaw("""
            UPDATE BrandIdentities
            SET Settings = json_remove(json_remove(Settings, '$.primaryAccent'), '$.surfaceColour')
            WHERE IsDefault = 1
              AND (json_extract(Settings, '$.primaryAccent') IS NOT NULL
                OR json_extract(Settings, '$.surfaceColour') IS NOT NULL)
            """);
    }
}
