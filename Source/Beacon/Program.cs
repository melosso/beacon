using System.Net;
using System.Text.Json;
using System.Threading.RateLimiting;
using Beacon;
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

    // Read only; nothing is written back until the keys below pass validation.
    var config = builder.Configuration.GetSection("Beacon");
    var decryptedConfig = ConfigurationEncryptor.ReadSensitiveValues(builder.Configuration, encryptionService);

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

    // Resolves public base URL for absolute email links. Priority: Beacon:PublicUrl > Beacon:ApiHosts (https) > request-derived.
    var resolvedPublicUrl = !string.IsNullOrWhiteSpace(publicUrl)
        ? publicUrl.TrimEnd('/')
        : hostOptions.ApiHosts.FirstOrDefault() is { } apiHost
            ? $"https://{apiHost.TrimEnd('/')}"
            : null;

    // Instance-level options (sourced from appsettings, not the db)
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

    // Validated: safe to write the encrypted form back to appsettings.json.
    ConfigurationEncryptor.PersistEncrypted(
        builder.Environment.ContentRootPath, encryptionService, decryptedConfig);

    var normalizedSigningKey = NormalizeKey(signingKey, 32);
    var normalizedEncryptionKey = NormalizeKey(encryptionKey, 32);

    builder.Services.Configure<Microsoft.Extensions.Hosting.HostOptions>(o =>
        o.ShutdownTimeout = TimeSpan.FromSeconds(30));

    // Service Registration
    builder.Services.AddBeaconStorage(databaseProvider, connectionString);

    builder.Services.AddSingleton(new TokenOptions
    {
        SigningKey = normalizedSigningKey,
        ExpiryDays = tokenExpiryDays,
        PayloadEncryptionKey = normalizedEncryptionKey
    });

    builder.Services.AddSingleton<TokenGenerator>(sp =>
        new TokenGenerator(sp.GetRequiredService<TokenOptions>()));

    builder.Services.AddSingleton<TokenValidator>(sp =>
        new TokenValidator(sp.GetRequiredService<TokenOptions>()));

    builder.Services.AddSingleton(new Encryptor(normalizedEncryptionKey));
    builder.Services.AddSingleton<EncryptionService>(encryptionService);
    builder.Services.AddSingleton(new EmailHasher(pepper));

    builder.Services.AddScoped<UserRepository>();
    builder.Services.AddScoped<BucketRepository>();
    builder.Services.AddScoped<IConsentRepository, ConsentRepository>();
    builder.Services.AddScoped<ConsentService>();
    builder.Services.AddScoped<TokenUsageRepository>();
    builder.Services.AddScoped<IWebhookRepository, WebhookRepository>();
    builder.Services.AddSingleton<AdminNotificationService>();
    builder.Services.AddSingleton<WebhookDeliveryQueue>();
    builder.Services.AddScoped<IWebhookService, WebhookService>();
    builder.Services.AddHostedService<Beacon.Api.WebhookDeliveryService>();

    builder.Services.AddScoped<ISubmissionFormRepository, SubmissionFormRepository>();
    builder.Services.AddScoped<SubmissionFormService>();
    builder.Services.AddSingleton<SubmissionRateLimiter>();
    builder.Services.AddSingleton<LoginLockout>();

    // Keyed on RemoteIpAddress, which UseForwardedHeaders resolves from trusted proxies only.
    // Reading X-Forwarded-For here would let any client pick its own bucket.
    builder.Services.AddRateLimiter(rateLimiter =>
    {
        rateLimiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

        rateLimiter.GlobalLimiter = PartitionedRateLimiter.CreateChained(
            PartitionedRateLimiter.Create<HttpContext, string>(context =>
                Partition(context, "global", 1500)),
            PartitionedRateLimiter.Create<HttpContext, string>(context =>
                context.Request.Path.StartsWithSegments("/api/admin/auth", StringComparison.OrdinalIgnoreCase)
                    ? Partition(context, "auth", 10)
                    : RateLimitPartition.GetNoLimiter("auth:skip")));

        rateLimiter.OnRejected = async (context, ct) =>
        {
            if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();

            await context.HttpContext.Response.WriteAsJsonAsync(new { error = "Rate limit exceeded" }, ct);
        };

        RateLimitPartition<string> Partition(HttpContext context, string scope, int permitLimit)
        {
            var clientIp = context.Connection.RemoteIpAddress;

            // A same-host reverse proxy makes every request look like loopback, so only skip in Development.
            if (builder.Environment.IsDevelopment() && (clientIp is null || IPAddress.IsLoopback(clientIp)))
                return RateLimitPartition.GetNoLimiter($"{scope}:loopback");

            return RateLimitPartition.GetFixedWindowLimiter(
                $"{scope}:{clientIp}",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permitLimit,
                    Window = TimeSpan.FromMinutes(1)
                });
        }
    });

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

    // BrandIdentities: singleton cache loaded from DB on startup
    builder.Services.AddSingleton<BrandIdentityService>(sp =>
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BeaconDbContext>();
        var service = new BrandIdentityService(sp.GetRequiredService<IServiceScopeFactory>());
        var identities = db.BrandIdentities
            .Include(i => i.BucketMappings)
            .OrderBy(i => i.IsDefault ? 0 : 1)
            .ThenBy(i => i.Name)
            .ToList();
        service.LoadInitialCache(identities);
        return service;
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

    // CORS Configuration
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

            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();

            var knownProxies = config["KnownProxies"];
            if (!string.IsNullOrWhiteSpace(knownProxies))
            {
                foreach (var entry in knownProxies.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (System.Net.IPNetwork.TryParse(entry, out var network))
                        options.KnownIPNetworks.Add(network);
                    else if (System.Net.IPAddress.TryParse(entry, out var address))
                        options.KnownProxies.Add(address);
                    else
                        Log.Warning("Ignoring unparseable Beacon:KnownProxies entry {Entry}", entry);
                }
            }
            else
            {
                // Default to private/loopback peers: a proxy on the same host or docker network.
                // Without this any client could spoof X-Forwarded-For and X-Forwarded-Host.
                foreach (var cidr in new[]
                    { "127.0.0.0/8", "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "::1/128", "fc00::/7" })
                {
                    options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(cidr));
                }
                Log.Information("Beacon:KnownProxies is unset; trusting forwarded headers from private and loopback peers only");
            }
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

    // Must run before rate limiting and host routing: both key off the resolved client IP / host.
    if (trustForwardedHeaders)
    {
        app.UseForwardedHeaders();
    }

    // Baseline security headers. No global CSP: the admin UI relies on inline scripts, and the
    // submission embed sets its own frame-ancestors. TryAdd so those routes keep their own values.
    app.Use(async (context, next) =>
    {
        var headers = context.Response.Headers;
        headers.TryAdd("X-Content-Type-Options", "nosniff");
        headers.TryAdd("Referrer-Policy", "no-referrer");
        headers.TryAdd("X-Frame-Options", "DENY");
        headers.TryAdd("Permissions-Policy", "geolocation=(), microphone=(), camera=()");
        await next();
    });

    app.UseCors("Default");

    app.UseRateLimiter();

    // Middleware pipeline
    if (enforceHttps)
    {
        app.UseHttpsRedirection();
        app.UseHsts();
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

    var webRoot = app.Environment.WebRootPath;

    // UI Endpoints (access controlled by HostRoutingMiddleware)
    app.MapGet("/admin", async context =>
    {
        if (!IsAuthenticated(context, jwtSigningKeyBytes))
        {
            context.Response.Redirect("/admin/login");
            return;
        }
        context.Response.ContentType = "text/html";
        var adminDir = Path.Combine(webRoot, "admin");
        string[] parts =
        [
            "_shell.html",
            "views/overview.html",
            "views/subscriptions.html",
            "views/bucket.html",
            "views/new-token.html",
            "views/new-bucket.html",
            "views/submissions.html",
            "views/submission-create.html",
            "views/submission-preview.html",
            "views/workflow.html",
            "views/audit.html",
            "views/settings.html",
            "modals/bucket-settings.html",
            "modals/bucket-mgmt.html",
            "modals/permissions.html",
            "modals/system-settings.html",
            "modals/share-audit.html",
            "modals/users-apikeys.html",
            "modals/brand-identity.html",
            "_footer.html",
        ];
        var ct = context.RequestAborted;
        foreach (var part in parts)
            await context.Response.WriteAsync(
                await File.ReadAllTextAsync(Path.Combine(adminDir, part), ct), ct);
    }).ExcludeFromDescription();

    app.MapGet("/admin/login", async (HttpContext context, ISystemConfigurationService configService) =>
    {
        if (IsAuthenticated(context, jwtSigningKeyBytes))
        {
            context.Response.Redirect("/admin");
            return;
        }
        var ct = context.RequestAborted;
        var html = await File.ReadAllTextAsync(Path.Combine(webRoot, "login.html"), ct);
        var loginCfg = configService.Get();
        if (loginCfg.LoginFooterEnabled && !string.IsNullOrWhiteSpace(loginCfg.LoginFooter))
            html = html.Replace("<!-- LOGIN_FOOTER_PLACEHOLDER -->", LoginFooterParser.ParseMarkdown(loginCfg.LoginFooter));
        if (loginCfg.PromoBarEnabled && loginCfg.PromoBarShowOnLogin && !string.IsNullOrWhiteSpace(loginCfg.PromoBar))
            html = html.Replace("<!-- LOGIN_PROMO_BAR_PLACEHOLDER -->", $"<div class=\"login-promo-bar\">{LoginFooterParser.ParseMarkdown(loginCfg.PromoBar)}</div>");
        else
            html = html.Replace("<!-- LOGIN_PROMO_BAR_PLACEHOLDER -->", string.Empty);
        context.Response.ContentType = "text/html";
        context.Response.Headers.Append("Cache-Control", "no-cache, no-store, must-revalidate");
        context.Response.Headers.Append("Pragma", "no-cache");
        await context.Response.WriteAsync(html, ct);
    }).ExcludeFromDescription();

    app.MapGet("/admin/logout", async (HttpContext context, ISystemConfigurationService configService) =>
    {
        var ct = context.RequestAborted;
        var html = await File.ReadAllTextAsync(Path.Combine(webRoot, "logout.html"), ct);
        var cfg = configService.Get();
        if (cfg.PromoBarEnabled && cfg.PromoBarShowOnLogin && !string.IsNullOrWhiteSpace(cfg.PromoBar))
            html = html.Replace("<!-- LOGIN_PROMO_BAR_PLACEHOLDER -->", $"<div class=\"login-promo-bar\">{LoginFooterParser.ParseMarkdown(cfg.PromoBar)}</div>");
        else
            html = html.Replace("<!-- LOGIN_PROMO_BAR_PLACEHOLDER -->", string.Empty);
        if (cfg.LoginFooterEnabled && !string.IsNullOrWhiteSpace(cfg.LoginFooter))
            html = html.Replace("<!-- LOGIN_FOOTER_PLACEHOLDER -->", LoginFooterParser.ParseMarkdown(cfg.LoginFooter));
        context.Response.ContentType = "text/html";
        context.Response.Headers.Append("Cache-Control", "no-cache, no-store, must-revalidate");
        context.Response.Headers.Append("Pragma", "no-cache");
        await context.Response.WriteAsync(html, ct);
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

    // Only routes whose path differs from the file on disk. Everything else under wwwroot
    // (favicon, robots.txt, css, fonts, js/*.js) is already served by UseStaticFiles above.
    app.MapGet("/openapi", ctx => ServeFile(ctx, Path.Combine(webRoot, "openapi.html"), "text/html"))
       .ExcludeFromDescription();

    app.MapGet("/js/admin.js", async (HttpContext context) =>
    {
        var parts = new[]
        {
            "core", "sidebar", "views", "records", "tokens",
            "webhooks", "submissions", "settings", "data-policies",
            "system-modals", "branding",
        };
        var ct = context.RequestAborted;
        context.Response.ContentType = "application/javascript";
        context.Response.Headers.Append("Cache-Control", "no-cache, no-store, must-revalidate");
        foreach (var part in parts)
        {
            var path = Path.Combine(webRoot, "js", "admin", $"{part}.js");
            await context.Response.WriteAsync(await File.ReadAllTextAsync(path, ct), ct);
        }
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
    app.MapFallback(async context =>
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
    Log.Information("Endpoints online: /api and /admin");
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


// Fails closed outside Development: shipped placeholder keys are publicly known.
static void ValidateSecureKey(string value, string keyName)
{
    if (string.IsNullOrWhiteSpace(value))
        throw new InvalidOperationException($"{keyName} is required.");

    if (IsInsecureDefault(value))
        throw new InvalidOperationException(
            $"{keyName} is still set to the shipped placeholder. Set it to a unique secret " +
            $"(for example: openssl rand -base64 32) before running outside Development.");

    if (value.Length < 32)
        throw new InvalidOperationException(
            $"{keyName} must be at least 32 characters. Generate one with: openssl rand -base64 32");
}

static string NormalizeKey(string key, int requiredBytes)
{
    Span<byte> buf = stackalloc byte[requiredBytes];
    if (Convert.TryFromBase64Chars(key, buf, out var written) && written == requiredBytes)
        return key;

    using var sha256 = System.Security.Cryptography.SHA256.Create();
    var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(key));
    return Convert.ToBase64String(hash);
}

static bool IsAuthenticated(HttpContext ctx, byte[] jwtKey) =>
    ctx.Request.Cookies.TryGetValue(JwtAuthHandler.CookieName, out var token) &&
    JwtAuthHandler.TryValidateToken(jwtKey, token, out _, out _, out _);

static Task ServeFile(HttpContext ctx, string path, string contentType, bool noCache = false, CancellationToken ct = default)
{
    if (!File.Exists(path))
    {
        ctx.Response.StatusCode = 404;
        return Task.CompletedTask;
    }
    ctx.Response.ContentType = contentType;
    if (noCache)
        ctx.Response.Headers.Append("Cache-Control", "no-cache, no-store, must-revalidate");
    return ctx.Response.SendFileAsync(path, ct);
}
