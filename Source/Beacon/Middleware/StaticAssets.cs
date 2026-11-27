using Microsoft.AspNetCore.StaticFiles;

namespace Beacon.Middleware;

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
