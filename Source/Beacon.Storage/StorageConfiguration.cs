using Beacon.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Beacon.Storage;

public static class StorageConfiguration
{
    public static IServiceCollection AddBeaconStorage(
        this IServiceCollection services,
        string provider,
        string connectionString)
    {
        services.AddDbContext<BeaconDbContext>(options =>
        {
            switch (provider.ToLowerInvariant())
            {
                case "sqlite":
                    options.UseSqlite(connectionString);
                    break;
                case "sqlserver":
                    options.UseSqlServer(connectionString);
                    break;
                case "postgres":
                case "postgresql":
                    options.UseNpgsql(connectionString);
                    break;
                case "mysql":
                    options.UseMySQL(connectionString);
                    break;
                default:
                    throw new ArgumentException($"Unsupported database provider: {provider}");
            }
        });

        services.AddScoped<ApiKeyRepository>();
        services.AddScoped<EmailQueueRepository>();
        services.AddScoped<BucketOptionsRepository>();
        services.AddScoped<EmailSenderService>();

        services.AddSingleton<EmailDispatchTrigger>();
        services.AddHostedService<EmailQueueWorker>();

        services.AddSingleton<DataPolicyTrigger>();
        services.AddScoped<WorkflowTaskRepository>();
        services.AddScoped<DataPolicyService>();
        services.AddHostedService<DataPolicyWorker>();
        
        services.AddHostedService<ConsentAuditBackfillService>();

        return services;
    }
}
