using System.Text.Json;
using Beacon.Api;
using Beacon.Configuration;
using Beacon.Core.Security;
using Beacon.Core.Services;
using Beacon.Middleware;
using Beacon.Security;
using Beacon.Storage;
using Beacon.Tokens;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.OpenApi;
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
    var enforceHttps = config.GetValue<bool>("EnforceHttps", false);
    var disableEmailNotifications = config.GetValue<bool>("DisableEmailNotifications", false);
    var publicUrl = config["PublicUrl"];
    var userAuthentication = config["UserAuthentication"] ?? "";

    // Host routing configuration
    var hostOptions = HostRoutingOptionsFactory.Create(builder.Configuration);
    builder.Services.AddSingleton(hostOptions);

    // Resolve the public base URL for building absolute confirmation links in emails.
    // Priority: explicit Beacon:PublicUrl → first entry in Beacon:ApiHosts (assumed https) → null (falls back to request-derived URL at runtime).
    var resolvedPublicUrl = !string.IsNullOrWhiteSpace(publicUrl)
        ? publicUrl.TrimEnd('/')
        : hostOptions.ApiHosts.FirstOrDefault() is { } apiHost
            ? $"https://{apiHost.TrimEnd('/')}"
            : null;

    // Instance-level options (sourced from appsettings, not the DB)
    var instanceOptions = new Beacon.Core.Services.InstanceOptions
    {
        DisableEmailNotifications = disableEmailNotifications,
        PublicUrl = resolvedPublicUrl
    };
    builder.Services.AddSingleton(instanceOptions);

    // Configure Kestrel to listen on both API and Admin ports
    builder.WebHost.ConfigureKestrel(options =>
    {
        options.ListenAnyIP(hostOptions.ApiPort);
        options.ListenAnyIP(hostOptions.AdminPort);
    });

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

    builder.Services.Configure<Microsoft.Extensions.Hosting.HostOptions>(o =>
        o.ShutdownTimeout = TimeSpan.FromSeconds(30));

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

    builder.Services.AddScoped<IUserRepository, UserRepository>();
    builder.Services.AddScoped<IBucketRepository, BucketRepository>();
    builder.Services.AddScoped<IConsentRepository, ConsentRepository>();
    builder.Services.AddScoped<IConsentService, ConsentService>();
    builder.Services.AddScoped<ITokenUsageRepository, TokenUsageRepository>();
    builder.Services.AddScoped<IWebhookRepository, WebhookRepository>();
    builder.Services.AddSingleton<IAdminNotificationService, AdminNotificationService>();
    builder.Services.AddSingleton<IWebhookDeliveryQueue, WebhookDeliveryQueue>();
    builder.Services.AddScoped<IWebhookService, WebhookService>();
    builder.Services.AddHostedService<Beacon.Api.WebhookDeliveryService>();

    builder.Services.AddScoped<ISubmissionFormRepository, SubmissionFormRepository>();
    builder.Services.AddScoped<ISubmissionFormService, SubmissionFormService>();
    builder.Services.AddSingleton<SubmissionRateLimiter>();

    // SystemConfiguration: singleton cache loaded lazily from DB on first use
    builder.Services.AddSingleton<ISystemConfigurationService>(sp =>
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BeaconDbContext>();
        var entity = db.SystemConfigurations.Find(1);
        var systemConfig = entity is not null
            ? JsonSerializer.Deserialize<SystemConfig>(entity.Configuration) ?? new SystemConfig()
            : new SystemConfig();
        return new SystemConfigurationService(sp.GetRequiredService<IServiceScopeFactory>(), systemConfig);
    });

    // Caching: resolved after SystemConfiguration is registered
    builder.Services.AddSingleton<IBeaconCacheService>(sp =>
    {
        var sysConfig = sp.GetRequiredService<ISystemConfigurationService>().Get();
        if (sysConfig.EnableCaching)
        {
            var memCache = sp.GetRequiredService<IMemoryCache>();
            return new Beacon.Services.MemoryBeaconCacheService(
                memCache,
                TimeSpan.FromSeconds(sysConfig.CacheTtlSeconds));
        }
        return new NullBeaconCacheService();
    });

    if (builder.Services.All(s => s.ServiceType != typeof(IMemoryCache)))
        builder.Services.AddMemoryCache();

    // Object storage: factory selects provider based on SystemConfiguration
    builder.Services.AddSingleton<IObjectStorageService>(sp =>
    {
        var sysConfig = sp.GetRequiredService<ISystemConfigurationService>().Get();
        return sysConfig is { ObjectStorage: true, ObjectStorageProvider: "s3" or "r2" or "minio" }
            ? ActivatorUtilities.CreateInstance<Beacon.Storage.S3ObjectStorageService>(sp)
            : ActivatorUtilities.CreateInstance<Beacon.Storage.LocalObjectStorageService>(sp);
    });

    builder.Services.AddHttpClient();

    builder.Services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = "CompositeScheme";
            options.DefaultChallengeScheme = "CompositeScheme";
            options.DefaultForbidScheme = "CompositeScheme";
        })
        .AddApiKeyAuth(options =>
        {
            options.AdminApiKey = adminApiKey;
            options.UserAuthentication = userAuthentication;
        })
        .AddJwtAuth(options =>
        {
            options.SigningKey = Convert.FromBase64String(normalizedSigningKey);
            options.UserAuthentication = userAuthentication;
        })
        .AddPolicyScheme("CompositeScheme", "API Key or JWT", options =>
        {
            options.ForwardDefaultSelector = context =>
            {
                // Bearer token → JWT handler
                if (context.Request.Headers.Authorization.ToString()
                    .StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    return JwtAuthExtensions.SchemeName;
                // HttpOnly auth cookie → JWT handler
                if (context.Request.Cookies.ContainsKey(JwtAuthHandler.CookieName))
                    return JwtAuthExtensions.SchemeName;
                // Otherwise → API key handler
                return ApiKeyAuthExtensions.SchemeName;
            };
        });

    builder.Services.AddAntiforgery();
    builder.Services.AddAuthorization(options =>
    {
        options.AddPolicy("Admin", policy => policy.RequireRole("admin"));

        foreach (var (policyName, scope) in new[]
        {
            ("ConsentRead",      "consent:read"),
            ("ConsentWrite",     "consent:write"),
            ("TokensWrite",      "tokens:write"),
            ("BucketsRead",      "buckets:read"),
            ("BucketsWrite",     "buckets:write"),
            ("SubmissionsRead",  "submissions:read"),
            ("SubmissionsWrite", "submissions:write"),
            ("AuditRead",        "audit:read"),
            ("WebhooksRead",     "webhooks:read"),
            ("WebhooksWrite",    "webhooks:write"),
        })
        {
            var capturedScope = scope;
            options.AddPolicy(policyName, p => p.RequireAssertion(ctx =>
                ctx.User.IsInRole("admin") ||
                ctx.User.IsInRole("user") ||
                ctx.User.HasClaim("beacon:permission", "_all") ||
                ctx.User.HasClaim("beacon:permission", capturedScope)));
        }
    });
    builder.Services.AddEndpointsApiExplorer();

    // .NET 9/10 OpenAPI Generation
    builder.Services.AddOpenApi(options =>
    {
        options.AddDocumentTransformer((document, context, cancellationToken) =>
        {
            document.Info.Title = "Beacon API";
            document.Info.Version = "v1";
            document.Info.Description = "A lightweight consent management platform. Handle email consent states independently from any ERP, CRM or platform.";

            // Add API key security scheme
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes["ApiKey"] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header,
                Name = "X-Api-Key",
                Description = "A mandatory API key for authentication."
            };

            // Set the host document for references to resolve correctly
            document.SetReferenceHostDocument();

            return Task.CompletedTask;
        });

        // Add security requirement to operations that need authorization
        options.AddOperationTransformer((operation, context, cancellationToken) =>
        {
            var hasAuthorization = context.Description.ActionDescriptor.EndpointMetadata
                .Any(m => m is Microsoft.AspNetCore.Authorization.AuthorizeAttribute ||
                         m is Microsoft.AspNetCore.Authorization.IAuthorizeData);

            if (hasAuthorization)
            {
                operation.Security ??= [];
                operation.Security.Add(new OpenApiSecurityRequirement
                {
                    {
                        new OpenApiSecuritySchemeReference("ApiKey"),
                        new List<string>()
                    }
                });
            }

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
                  .AllowAnyHeader()
                  .AllowCredentials(); // Required for cross-origin cookie (admin port → API port)
        });

        // Permissive policy for public submission endpoints (it'll validate origins by checking the submission form's allowed origins)
        options.AddPolicy("PublicSubmission", policy =>
        {
            policy.AllowAnyOrigin()
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

            // Clear both to ensure no internal IP filtering occurs
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear(); 
            
            // Ensure we process headers from the tunneling proxy
            options.ForwardLimit = null; 
        });
    }

    var app = builder.Build();

    // Database Initialization
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<BeaconDbContext>();
        DatabaseMigrator.Initialize(db);

    }

    // Add Serilog request logging
    app.UseSerilogRequestLogging();

    // Exception handler runs before CORS so error responses still get CORS headers
    app.UseExceptionHandler(error => error.Run(async context =>
    {
        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new { error = "An internal server error occurred." });
    }));

    app.UseCors("Default");

    app.UseRateLimiting(options =>
    {
        options.MaxRequests = 1500;
        options.Window = TimeSpan.FromMinutes(1);
        // Strict limit for auth endpoints to mitigate brute-force attacks
        options.StrictPaths["/api/admin/auth"] = 10;
    });

    // Middleware pipeline
    if (enforceHttps)
    {
        app.UseHttpsRedirection();
        app.UseHsts();
    }

    if (trustForwardedHeaders)
    {
        app.UseForwardedHeaders();
    }

    app.UseHostRouting();
    app.UseStaticFiles(new StaticFileOptions
    {
        OnPrepareResponse = ctx =>
        {
            var ext = Path.GetExtension(ctx.File.Name).ToLowerInvariant();
            if (ext is ".js" or ".css" or ".html")
            {
                ctx.Context.Response.Headers.Append("Cache-Control", "no-cache, no-store, must-revalidate");
                ctx.Context.Response.Headers.Append("Pragma", "no-cache");
                ctx.Context.Response.Headers.Append("Expires", "0");
            }
        }
    });
    app.UseAuthentication();
    app.UseAuthorization();

    app.UseAntiforgery();

    // Endpoint Mapping
    app.MapOpenApi(); // Maps /openapi/v1.json
    app.MapConsentEndpoints();
    app.MapAdminEndpoints();
    app.MapAuthEndpoints();
    app.MapUserEndpoints();
    app.MapApiKeyEndpoints();
    app.MapSubmissionEndpoints();

    // Health check endpoint
    app.MapGet("/health", async (BeaconDbContext db) =>
    {
        try
        {
            // Test database connectivity
            await db.Database.CanConnectAsync();
            return Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow });
        }
        catch
        {
            return Results.Json(new { status = "unhealthy", timestamp = DateTime.UtcNow }, statusCode: 503);
        }
    }).ExcludeFromDescription();

    // Resolve the JWT signing key bytes once for reuse in route handlers below
    var jwtSigningKeyBytes = Convert.FromBase64String(normalizedSigningKey);

    // UI Endpoints (access controlled by HostRoutingMiddleware)
    app.MapGet("/admin", async (HttpContext context) =>
    {
        // Validate auth cookie; redirect to login if missing or expired
        if (!context.Request.Cookies.TryGetValue(JwtAuthHandler.CookieName, out var cookieToken) ||
            !JwtAuthHandler.TryValidateToken(jwtSigningKeyBytes, cookieToken, out _, out _, out _))
        {
            context.Response.Redirect("/admin/login");
            return;
        }
        context.Response.ContentType = "text/html";
        await context.Response.SendFileAsync(
            Path.Combine(app.Environment.WebRootPath, "admin.html"));
    }).ExcludeFromDescription();

    app.MapGet("/admin/login", async (HttpContext context) =>
    {
        // Already authenticated; skip the login page
        if (context.Request.Cookies.TryGetValue(JwtAuthHandler.CookieName, out var cookieToken) &&
            JwtAuthHandler.TryValidateToken(jwtSigningKeyBytes, cookieToken, out _, out _, out _))
        {
            context.Response.Redirect("/admin");
            return;
        }
        context.Response.ContentType = "text/html";
        await context.Response.SendFileAsync(
            Path.Combine(app.Environment.WebRootPath, "login.html"));
    }).ExcludeFromDescription();

    app.MapGet("/admin/logout", async (HttpContext context) =>
    {
        context.Response.ContentType = "text/html";
        await context.Response.SendFileAsync(
            Path.Combine(app.Environment.WebRootPath, "logout.html"));
    }).ExcludeFromDescription();

    app.MapGet("/admin/config.js", (HttpContext context, HostRoutingOptions routingOptions) =>
    {
        var host = context.Request.Host.Host.ToLowerInvariant();
        
        var primaryApiHost = routingOptions.ApiHosts.FirstOrDefault()?
            .Replace("https://", "", StringComparison.OrdinalIgnoreCase)
            .Replace("http://", "", StringComparison.OrdinalIgnoreCase);

        string apiBase;
        bool isProductionHost = routingOptions.AdminHosts.Contains(host) || 
                                routingOptions.ApiHosts.Contains(host);

        if (isProductionHost && !string.IsNullOrEmpty(primaryApiHost))
        {
            apiBase = $"https://{primaryApiHost}";
        }
        else
        {
            apiBase = $"{context.Request.Scheme}://{host}:{routingOptions.ApiPort}";
        }

        var publicUrl = !string.IsNullOrEmpty(primaryApiHost) ? $"https://{primaryApiHost}" : "";
        var js = string.Join("\n", [
            $"const API_BASE = {JsonSerializer.Serialize(apiBase)};",
            $"const PUBLIC_URL = {JsonSerializer.Serialize(publicUrl)};",
            $"const DEFAULT_EXPIRY_DAYS = {tokenExpiryDays};",
            $"const DISABLE_EMAIL_NOTIFICATIONS = {(disableEmailNotifications ? "true" : "false")};",
            $"const USER_AUTH_METHOD = {JsonSerializer.Serialize(userAuthentication)};"
        ]);

        context.Response.Headers.Append("Cache-Control", "no-cache, no-store, must-revalidate");
        context.Response.Headers.Append("Expires", "0");
        context.Response.Headers.Append("X-Content-Type-Options", "nosniff");

        return Results.Content(js, "application/javascript");
    }).ExcludeFromDescription();

    app.MapGet("/openapi", async context =>
    {
        context.Response.ContentType = "text/html";
        await context.Response.SendFileAsync(
            Path.Combine(app.Environment.WebRootPath, "openapi.html"));
    }).ExcludeFromDescription();

    app.MapGet("/favicon.ico", async context =>
    {
        var path = Path.Combine(app.Environment.WebRootPath, "favicon.ico");
        if (File.Exists(path))
        {
            context.Response.ContentType = "image/x-icon";
            await context.Response.SendFileAsync(path);
        }
        else
        {
            context.Response.StatusCode = 404;
        }
    });

    // include   <link href="/css/site.css" rel="stylesheet" />
    app.MapGet("/css/site.css", async context =>
    {
        var path = Path.Combine(app.Environment.WebRootPath, "css", "site.css");
        if (File.Exists(path))
        {
            context.Response.ContentType = "text/css";
            await context.Response.SendFileAsync(path);
        }   
        else
        {
            context.Response.StatusCode = 404;
        }
    });

    app.MapGet("/robots.txt", async context =>
    {
        var path = Path.Combine(app.Environment.WebRootPath, "robots.txt");
        if (File.Exists(path))
        {
            context.Response.ContentType = "text/plain";
            await context.Response.SendFileAsync(path);
        }
        else
        {
            context.Response.StatusCode = 404;
        }
    });

    app.MapGet("/js/auth.js", async context =>
    {
        var path = Path.Combine(app.Environment.WebRootPath, "js", "auth.js");
        if (File.Exists(path))
        {
            context.Response.ContentType = "application/javascript";
            context.Response.Headers.Append("Cache-Control", "no-cache, no-store, must-revalidate");
            await context.Response.SendFileAsync(path);
        }
        else { context.Response.StatusCode = 404; }
    }).ExcludeFromDescription();

    app.MapGet("/js/admin.js", async context =>
    {
        var path = Path.Combine(app.Environment.WebRootPath, "js", "admin.js");
        if (File.Exists(path))
        {
            context.Response.ContentType = "application/javascript";
            context.Response.Headers.Append("Cache-Control", "no-cache, no-store, must-revalidate");
            await context.Response.SendFileAsync(path);
        }
        else { context.Response.StatusCode = 404; }
    }).ExcludeFromDescription();

    app.MapGet("/", async context =>
    {
        var routingOptions = context.RequestServices.GetRequiredService<HostRoutingOptions>();
        var host = context.Request.Host.Host.ToLowerInvariant();
        var port = context.Connection.LocalPort;

        // Determine if this is an admin context
        bool isAdminContext;
        if (routingOptions.UseHostBasedRouting &&
            (routingOptions.AdminHosts.Contains(host) || routingOptions.ApiHosts.Contains(host) ||
             host == "localhost" || host == "127.0.0.1"))
        {
            isAdminContext = routingOptions.AdminHosts.Contains(host) ||
                             host == "localhost" || host == "127.0.0.1";
        }
        else
        {
            // Port-based (or unknown host fallback): use port to determine context
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
                var notFoundPath = Path.Combine(app.Environment.WebRootPath, "404.html");
                if (File.Exists(notFoundPath))
                {
                    context.Response.ContentType = "text/html";
                    await context.Response.SendFileAsync(notFoundPath);
                }
            }
        }
    }).ExcludeFromDescription();

    // Catch-all: serve 404 page for any unmatched routes (including file paths)
    app.MapFallback("{**path}", async context =>
    {
        context.Response.StatusCode = 404;
        var notFoundPath = Path.Combine(app.Environment.WebRootPath, "404.html");
        if (File.Exists(notFoundPath))
        {
            context.Response.ContentType = "text/html";
            await context.Response.SendFileAsync(notFoundPath);
        }
    }).ExcludeFromDescription();

    // Log startup information
    if (hostOptions.UseHostBasedRouting)
    {
        Log.Information("Beacon is spinning up!");
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
        Log.Information("Exit: Application stopped");
    });

    app.Run();

    Log.Information("Application shutdown complete");
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
