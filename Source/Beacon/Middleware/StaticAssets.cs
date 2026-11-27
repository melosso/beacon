using Microsoft.AspNetCore.StaticFiles;

namespace Beacon.Middleware;

/// <summary>Content types served from wwwroot. Any other extension returns 404.</summary>
public static class StaticAssets
{
    public static readonly FileExtensionContentTypeProvider ContentTypes = new(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".css"] = "text/css",
        [".html"] = "text/html",
        [".ico"] = "image/x-icon",
        [".js"] = "text/javascript",
        [".svg"] = "image/svg+xml",
        [".woff2"] = "font/woff2",
    });
}
