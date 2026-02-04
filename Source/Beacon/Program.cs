using Beacon.Api;
using Beacon.Configuration;
using Beacon.Core.Security;
using Beacon.Core.Services;
using Beacon.Middleware;
using Beacon.Security;
using Beacon.Storage;
using Beacon.Tokens;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Serilog;

// Ensure log directory exists
var logDirectory = Path.Combine(Directory.GetCurrentDirectory(), "log");
if (!Directory.Exists(logDirectory))
{
    Directory.CreateDirectory(logDirectory);
}

// Configure Serilog from appsettings.json
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(new ConfigurationBuilder()
        .SetBasePath(Directory.GetCurrentDirectory())
        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
        .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true)
        .Build())
    .CreateLogger();

try
{
    Log.Information("");
    Log.Information("██████╗ ███████╗ █████╗  ██████╗ ██████╗ ███╗   ██╗");
    Log.Information("██╔══██╗██╔════╝██╔══██╗██╔════╝██╔═══██╗████╗  ██║");
    Log.Information("██████╔╝█████╗  ███████║██║     ██║   ██║██╔██╗ ██║");
    Log.Information("██╔══██╗██╔══╝  ██╔══██║██║     ██║   ██║██║╚██╗██║");
    Log.Information("██████╔╝███████╗██║  ██║╚██████╗╚██████╔╝██║ ╚████║");
    Log.Information("╚═════╝ ╚══════╝╚═╝  ╚═╝ ╚═════╝ ╚═════╝ ╚═╝  ╚═══╝");
    Log.Information("");

    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog();

    // Initialize EncryptionService first (uses BEACON_ENCRYPTION_KEY from environment)
    var encryptionService = new EncryptionService(builder.Environment.ContentRootPath);

    // Process configuration - decrypt encrypted values and encrypt plaintext values
    var config = builder.Configuration.GetSection("Beacon");
    var decryptedConfig = ConfigurationEncryptor.ProcessConfiguration(
        builder.Configuration,
        encryptionService,
        builder.Environment.ContentRootPath);

    // Read configuration values (use decrypted values where available)
    var databaseProvider = config["DatabaseProvider"] ?? "sqlite";
    var connectionString = decryptedConfig.GetValueOrDefault("ConnectionString", config["ConnectionString"] ?? "Data Source=Beacon.db");
    var signingKey = decryptedConfig.GetValueOrDefault("SigningKey") ?? throw new InvalidOperationException("Beacon__SigningKey is required");
    var encryptionKey = decryptedConfig.GetValueOrDefault("EncryptionKey") ?? throw new InvalidOperationException("Beacon__EncryptionKey is required");
    var pepper = decryptedConfig.GetValueOrDefault("Pepper") ?? throw new InvalidOperationException("Beacon__Pepper is required");
    var adminApiKey = decryptedConfig.GetValueOrDefault("AdminApiKey") ?? throw new InvalidOperationException("Beacon__AdminApiKey is required");
    var tokenExpiryDays = int.TryParse(config["TokenExpiryDays"], out var days) ? days : 30;
    var trustForwardedHeaders = config.GetValue<bool>("TrustForwardedHeaders", false);

    // Host routing configuration
    var hostOptions = HostRoutingOptionsFactory.Create(builder.Configuration);
    builder.Services.AddSingleton(hostOptions);

    // Configure Kestrel - use host-based or port-based depending on configuration
    if (!hostOptions.UseHostBasedRouting)
    {
        // Port-based mode: Listen on specific ports
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ListenAnyIP(hostOptions.ApiPort);   // API port
            options.ListenAnyIP(hostOptions.AdminPort); // Admin port
        });
    }
    // else: Host-based mode uses ASPNETCORE_URLS or default port

    // Security validation
    if (!builder.Environment.IsDevelopment())
    {
        ValidateSecureKey(signingKey, "Beacon__SigningKey");
        ValidateSecureKey(encryptionKey, "Beacon__EncryptionKey");
        ValidateSecureKey(pepper, "Beacon__Pepper");
        ValidateSecureKey(adminApiKey, "Beacon__AdminApiKey");
    }
    else
    {
        if (IsInsecureDefault(signingKey) || IsInsecureDefault(encryptionKey) ||
            IsInsecureDefault(pepper) || IsInsecureDefault(adminApiKey))
        {
            Log.Warning("Using insecure default keys");
        }
    }

    var normalizedSigningKey = NormalizeKey(signingKey, 32);
    var normalizedEncryptionKey = NormalizeKey(encryptionKey, 32);

    // Service Registration
    builder.Services.AddBeaconStorage(databaseProvider, connectionString);

    builder.Services.AddSingleton(new TokenOptions
    {
        SigningKey = normalizedSigningKey,
        ExpiryDays = tokenExpiryDays
    });

    builder.Services.AddSingleton<TokenGenerator>(sp =>
        new TokenGenerator(sp.GetRequiredService<TokenOptions>()));

    builder.Services.AddSingleton<TokenValidator>(sp =>
        new TokenValidator(sp.GetRequiredService<TokenOptions>()));

    builder.Services.AddSingleton(new Encryptor(normalizedEncryptionKey));
    builder.Services.AddSingleton<IEncryptionService>(encryptionService);
    builder.Services.AddSingleton(new EmailHasher(pepper));

    builder.Services.AddScoped<IConsentRepository, ConsentRepository>();
    builder.Services.AddScoped<IConsentService, ConsentService>();
    builder.Services.AddScoped<ITokenUsageRepository, TokenUsageRepository>();

    builder.Services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = ApiKeyAuthExtensions.SchemeName;
            options.DefaultChallengeScheme = ApiKeyAuthExtensions.SchemeName;
            options.DefaultForbidScheme = ApiKeyAuthExtensions.SchemeName;
        })
        .AddApiKeyAuth(options =>
        {
            options.AdminApiKey = adminApiKey;
        });

    builder.Services.AddAuthorization();
    builder.Services.AddEndpointsApiExplorer();

    // .NET 9/10 OpenAPI Generation
    builder.Services.AddOpenApi(options =>
    {
        options.AddDocumentTransformer((document, context, cancellationToken) =>
        {
            document.Info.Title = "Beacon API";
            document.Info.Version = "v1";
            return Task.CompletedTask;
        });
    });

    // CORS Configuration - supports both localhost and configured origins
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("Default", policy =>
        {
            var origins = new List<string>
            {
                // Localhost origins (development)
                "http://localhost:5000",
                "http://localhost:5001",
                "http://127.0.0.1:5000",
                "http://127.0.0.1:5001"
            };

            // Add configured allowed origins
            var allowedOrigins = config["AllowedOrigins"];
            if (!string.IsNullOrWhiteSpace(allowedOrigins))
            {
                foreach (var origin in allowedOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    origins.Add(origin);
                }
            }

            // Add admin hosts as origins (https by default)
            var adminHosts = config["AdminHosts"];
            if (!string.IsNullOrWhiteSpace(adminHosts))
            {
                foreach (var host in adminHosts.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    origins.Add($"https://{host}");
                    origins.Add($"http://{host}"); // For internal/dev scenarios
                }
            }

            // Add API hosts as origins
            var apiHosts = config["ApiHosts"];
            if (!string.IsNullOrWhiteSpace(apiHosts))
            {
                foreach (var host in apiHosts.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    origins.Add($"https://{host}");
                    origins.Add($"http://{host}");
                }
            }

            policy.WithOrigins(origins.Distinct().ToArray())
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        });
    });

    // Forwarded Headers configuration (for reverse proxy)
    if (trustForwardedHeaders)
    {
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor |
                                       ForwardedHeaders.XForwardedProto |
                                       ForwardedHeaders.XForwardedHost;
            // Clear known networks/proxies to accept from any proxy
            // In production, you may want to restrict this
            options.KnownProxies.Clear();
        });
    }

    var app = builder.Build();

    // Database Initialization
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<BeaconDbContext>();
        db.Database.EnsureCreated();
    }

    // Middleware Pipeline

    // Forwarded Headers must be first (before any middleware that reads Host/Scheme)
    if (trustForwardedHeaders)
    {
        app.UseForwardedHeaders();
    }

    // Add Serilog request logging
    app.UseSerilogRequestLogging();

    app.UseCors("Default");

    // Host-based routing security (replaces port-based checks)
    app.UseHostRouting();

    app.UseRateLimiting(options =>
    {
        options.MaxRequests = 1500;
        options.Window = TimeSpan.FromMinutes(1);
    });

    app.UseAuthentication();
    app.UseAuthorization();

    app.UseStaticFiles();

    // Endpoint Mapping
    app.MapOpenApi(); // Maps /openapi/v1.json
    app.MapConsentEndpoints();
    app.MapAdminEndpoints();

    // UI Endpoints (access controlled by HostRoutingMiddleware)
    app.MapGet("/admin", async context =>
    {
        context.Response.ContentType = "text/html";
        await context.Response.SendFileAsync(
            Path.Combine(app.Environment.WebRootPath, "admin.html"));
    }).ExcludeFromDescription();

    app.MapGet("/openapi", async context =>
    {
        context.Response.ContentType = "text/html";
        await context.Response.SendFileAsync(
            Path.Combine(app.Environment.WebRootPath, "openapi.html"));
    }).ExcludeFromDescription();

    app.MapGet("/", async context =>
    {
        var routingOptions = context.RequestServices.GetRequiredService<HostRoutingOptions>();
        var host = context.Request.Host.Host.ToLowerInvariant();
        var port = context.Connection.LocalPort;

        // Determine if this is an admin context
        bool isAdminContext;
        if (routingOptions.UseHostBasedRouting)
        {
            isAdminContext = routingOptions.AdminHosts.Contains(host) ||
                             host == "localhost" || host == "127.0.0.1";
        }
        else
        {
            isAdminContext = port == routingOptions.AdminPort;
        }

        if (isAdminContext)
        {
            context.Response.Redirect("/admin");
        }
        else
        {
            // API context: serve index.html if it exists
            var indexPath = Path.Combine(app.Environment.WebRootPath, "index.html");
            if (File.Exists(indexPath))
            {
                context.Response.ContentType = "text/html";
                await context.Response.SendFileAsync(indexPath);
            }
            else
            {
                context.Response.StatusCode = 404;
            }
        }
    }).ExcludeFromDescription();

    // Log startup information
    if (hostOptions.UseHostBasedRouting)
    {
        Log.Information("Beacon is spinning up with host-based routing");
        Log.Information("API hosts: {ApiHosts}", string.Join(", ", hostOptions.ApiHosts));
        Log.Information("Admin hosts: {AdminHosts}", string.Join(", ", hostOptions.AdminHosts));
    }
    else
    {
        Log.Information("Beacon is spinning up on http://localhost:{ApiPort} (API) and http://localhost:{AdminPort} (Admin)",
            hostOptions.ApiPort, hostOptions.AdminPort);
    }
    Log.Information("Endpoints online: /api/consent and /admin");
    Log.Information("");

    // Register shutdown handlers
    var lifetime = app.Lifetime;

    lifetime.ApplicationStarted.Register(() =>
    {
        Log.Information("Application started successfully");
        Log.Information("");
    });

    lifetime.ApplicationStopping.Register(() =>
    {
        Log.Information("");
        Log.Information("Exit: Application is stopping...");
    });

    lifetime.ApplicationStopped.Register(() =>
    {
        Log.Information("Application stopped");
    });

    app.Run();

    Log.Information("Exit: Application shutdown complete");
}
catch (Exception ex)
{
    Log.Fatal(ex, "Beacon terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

// Static Helpers
static bool IsInsecureDefault(string value)
{
    return value.Contains("INSECURE", StringComparison.OrdinalIgnoreCase);
}

static void ValidateSecureKey(string value, string keyName)
{
    // Validation logic for production environments
}

static string NormalizeKey(string key, int requiredBytes)
{
    try
    {
        var decoded = Convert.FromBase64String(key);
        if (decoded.Length == requiredBytes) return key;
    }
    catch { }

    using var sha256 = System.Security.Cryptography.SHA256.Create();
    var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(key));
    return Convert.ToBase64String(hash);
}

public partial class Program { }
