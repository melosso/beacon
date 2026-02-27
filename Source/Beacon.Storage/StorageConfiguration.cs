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
                    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));
                    break;
                default:
                    throw new ArgumentException($"Unsupported database provider: {provider}");
            }
        });

        services.AddScoped<IEmailQueueRepository, EmailQueueRepository>();
        services.AddScoped<IBucketOptionsRepository, BucketOptionsRepository>();
        services.AddScoped<IEmailSenderService, EmailSenderService>();
        services.AddSingleton<EmailDispatchTrigger>();
        services.AddHostedService<EmailQueueWorker>();

        return services;
    }
}
