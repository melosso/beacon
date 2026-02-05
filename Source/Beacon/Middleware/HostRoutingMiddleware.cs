namespace Beacon.Middleware;

public class HostRoutingOptions
{
    /// <summary>
    /// Hosts allowed to access public API endpoints (e.g., beacon-api.melosso.com)
    /// </summary>
    public HashSet<string> ApiHosts { get; set; } = [];

    /// <summary>
    /// Hosts allowed to access admin endpoints (e.g., beacon-admin.melosso.com)
    /// </summary>
    public HashSet<string> AdminHosts { get; set; } = [];

    /// <summary>
    /// Port for API access (fallback when hosts not configured)
    /// </summary>
    public int ApiPort { get; set; } = 5000;

    /// <summary>
    /// Port for Admin access (fallback when hosts not configured)
    /// </summary>
    public int AdminPort { get; set; } = 5001;

    /// <summary>
    /// When true, uses host-based routing. When false, uses port-based routing.
    /// Automatically determined based on whether ApiHosts/AdminHosts are configured.
    /// </summary>
    public bool UseHostBasedRouting => ApiHosts.Count > 0 || AdminHosts.Count > 0;
}

public class HostRoutingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly HostRoutingOptions _options;
    private readonly ILogger<HostRoutingMiddleware> _logger;
    private readonly string? _notFoundHtml;

    // Admin-only paths that should never be accessible from API host
    private static readonly string[] AdminPaths = ["/admin", "/openapi"];

    public HostRoutingMiddleware(
        RequestDelegate next,
        HostRoutingOptions options,
        ILogger<HostRoutingMiddleware> logger,
        IWebHostEnvironment env)
    {
        _next = next;
        _options = options;
        _logger = logger;

        var notFoundPath = Path.Combine(env.WebRootPath, "404.html");
        _notFoundHtml = File.Exists(notFoundPath) ? File.ReadAllText(notFoundPath) : null;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "/";

        if (_options.UseHostBasedRouting)
        {
            if (!ValidateHostBasedAccess(context, path))
            {
                await WriteNotFound(context);
                return;
            }
        }
        else
        {
            if (!ValidatePortBasedAccess(context, path))
            {
                await WriteNotFound(context);
                return;
            }
        }

        await _next(context);
    }

    private async Task WriteNotFound(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        if (_notFoundHtml != null)
        {
            context.Response.ContentType = "text/html";
            await context.Response.WriteAsync(_notFoundHtml);
        }
    }

    private bool ValidateHostBasedAccess(HttpContext context, string path)
    {
        // Get the host from the request (respects X-Forwarded-Host if ForwardedHeaders middleware is used)
        var host = context.Request.Host.Host.ToLowerInvariant();

        // Check if this is an admin-only path
        var isAdminPath = AdminPaths.Any(p =>
            path.Equals(p, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(p + "/", StringComparison.OrdinalIgnoreCase));

        // Admin host - allow everything
        if (_options.AdminHosts.Contains(host))
        {
            return true;
        }

        // API host - block admin paths
        if (_options.ApiHosts.Contains(host))
        {
            if (isAdminPath)
            {
                _logger.LogWarning("Blocked admin path {Path} access from API host {Host}", path, host);
                return false;
            }
            return true;
        }

        // Localhost/loopback - allow everything (development)
        if (IsLocalhost(host))
        {
            return true;
        }

        // Unknown host - fall back to port-based validation
        // This allows direct access via internal hostnames (e.g. Docker service names)
        // while still enforcing port-based separation for admin/api paths
        _logger.LogDebug("Unknown host {Host}, falling back to port-based validation", host);
        return ValidatePortBasedAccess(context, path);
    }

    private bool ValidatePortBasedAccess(HttpContext context, string path)
    {
        var port = context.Connection.LocalPort;

        // Check if this is an admin-only path
        var isAdminPath = AdminPaths.Any(p =>
            path.Equals(p, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(p + "/", StringComparison.OrdinalIgnoreCase));

        // Admin port - allow everything
        if (port == _options.AdminPort)
        {
            return true;
        }

        // API port - block admin paths
        if (port == _options.ApiPort)
        {
            if (isAdminPath)
            {
                return false;
            }
            return true;
        }

        // Any other port in port-based mode - allow everything (handles single-port dev scenarios)
        return true;
    }

    private static bool IsLocalhost(string host)
    {
        return host == "localhost" ||
               host == "127.0.0.1" ||
               host == "::1" ||
               host.EndsWith(".localhost");
    }
}

public static class HostRoutingOptionsFactory
{
    public static HostRoutingOptions Create(IConfiguration configuration)
    {
        var options = new HostRoutingOptions();

        // Parse comma-separated host lists
        var apiHosts = configuration["Beacon:ApiHosts"];
        var adminHosts = configuration["Beacon:AdminHosts"];

        if (!string.IsNullOrWhiteSpace(apiHosts))
        {
            foreach (var host in apiHosts.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                options.ApiHosts.Add(host.ToLowerInvariant());
            }
        }

        if (!string.IsNullOrWhiteSpace(adminHosts))
        {
            foreach (var host in adminHosts.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                options.AdminHosts.Add(host.ToLowerInvariant());
            }
        }

        // Port configuration (fallback)
        if (int.TryParse(configuration["Beacon:ApiPort"], out var apiPort))
        {
            options.ApiPort = apiPort;
        }

        if (int.TryParse(configuration["Beacon:AdminPort"], out var adminPort))
        {
            options.AdminPort = adminPort;
        }

        return options;
    }
}

public static class HostRoutingExtensions
{
    public static IApplicationBuilder UseHostRouting(this IApplicationBuilder app)
    {
        return app.UseMiddleware<HostRoutingMiddleware>();
    }
}
