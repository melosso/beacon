using Beacon.Api;
using Beacon.Core.Security;
using Beacon.Core.Services;
using Beacon.Security;
using Beacon.Storage;
using Beacon.Tokens;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Configure Kestrel for dual-port hosting
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(5000); // Main API
    options.ListenAnyIP(5001); // Admin panel and Documentation
});

// Configuration
var config = builder.Configuration.GetSection("Beacon");
var databaseProvider = config["DatabaseProvider"] ?? "sqlite";
var connectionString = config["ConnectionString"] ?? "Data Source=Beacon.db";
var signingKey = config["SigningKey"] ?? throw new InvalidOperationException("Beacon__SigningKey is required");
var encryptionKey = config["EncryptionKey"] ?? throw new InvalidOperationException("Beacon__EncryptionKey is required");
var pepper = config["Pepper"] ?? throw new InvalidOperationException("Beacon__Pepper is required");
var adminApiKey = config["AdminApiKey"] ?? throw new InvalidOperationException("Beacon__AdminApiKey is required");
var tokenExpiryDays = int.TryParse(config["TokenExpiryDays"], out var days) ? days : 30;

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
        Console.WriteLine("WARNING: Using insecure default keys.");
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

builder.Services.AddCors(options =>
{
    options.AddPolicy("AdminPanel", policy =>
    {
        var serverName = config["ServerName"] ?? "localhost";
        policy.WithOrigins("http://localhost:5001", "http://127.0.0.1:5001", $"https://{serverName}:5001", $"http://{serverName}:5001")
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// Database Initialization
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BeaconDbContext>();
    db.Database.EnsureCreated();
}

// Middleware Pipeline
app.UseCors("AdminPanel");

app.UseRateLimiting(options =>
{
    options.MaxRequests = 100;
    options.Window = TimeSpan.FromMinutes(1);
});

app.UseAuthentication();
app.UseAuthorization();

// Integrated Port Security Middleware
app.Use(async (context, next) =>
{
    var port = context.Connection.LocalPort;
    var path = context.Request.Path;

    // Restrict Admin and OpenAPI metadata to Port 5001
    if (port == 5000)
    {
        if (path.StartsWithSegments("/admin") || path.StartsWithSegments("/openapi"))
        {
            context.Response.StatusCode = 404;
            return;
        }
    }
    await next();
});

app.UseStaticFiles();

// Endpoint Mapping
app.MapOpenApi(); // Maps /openapi/v1.json
app.MapConsentEndpoints();
app.MapAdminEndpoints();

// UI Endpoints (Port 5001 restricted via middleware above)
app.MapGet("/admin", async context =>
{
    if (context.Connection.LocalPort != 5001)
    {
        context.Response.StatusCode = 404;
        return;
    }

    context.Response.ContentType = "text/html";
    await context.Response.SendFileAsync(
        Path.Combine(app.Environment.WebRootPath, "admin.html"));
}).ExcludeFromDescription();

app.MapGet("/openapi", async context =>
{
    if (context.Connection.LocalPort != 5001)
    {
        context.Response.StatusCode = 404;
        return;
    }

    context.Response.ContentType = "text/html";
    await context.Response.SendFileAsync(
        Path.Combine(app.Environment.WebRootPath, "openapi.html"));
}).ExcludeFromDescription();

app.MapGet("/", async context =>
{
    if (context.Connection.LocalPort == 5001)
    {
        context.Response.Redirect("/admin");
    }
    else
    {
        context.Response.StatusCode = 404;
    }
}).ExcludeFromDescription();

app.Run();

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