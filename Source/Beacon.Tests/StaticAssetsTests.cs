using Beacon.Middleware;
using Xunit;

namespace Beacon.Tests;

public class StaticAssetsTests
{
    [Theory]
    [InlineData("/lib/README.md")]
    [InlineData("/fonts/IBM-Plex-OFL.txt")]
    [InlineData("/lib/cally/LICENSE")]
    [InlineData("/Beacon.db")]
    public void ContentTypes_RejectDocsAndLicenses(string path) =>
        Assert.False(StaticAssets.ContentTypes.TryGetContentType(path, out _));

    [Theory]
    [InlineData("/js/base.js")]
    [InlineData("/css/site.css")]
    [InlineData("/fonts/ibm-plex-sans-latin-400-normal.woff2")]
    [InlineData("/favicon.svg")]
    public void ContentTypes_ServeAssets(string path) =>
        Assert.True(StaticAssets.ContentTypes.TryGetContentType(path, out _));
}
