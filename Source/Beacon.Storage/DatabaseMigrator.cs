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
        }
    }
}
