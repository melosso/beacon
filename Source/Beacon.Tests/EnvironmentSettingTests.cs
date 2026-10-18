using Beacon.Core.Services;
using Xunit;

namespace Beacon.Tests;

public class EnvironmentSettingTests
{
    private static string? Resolve(string key, params (string Name, string Value)[] env)
    {
        var map = env.ToDictionary(e => e.Name, e => e.Value);
        return EnvironmentSetting.Resolve(key, map.GetValueOrDefault);
    }

    [Theory]
    [InlineData("SigningKey", "SIGNING_KEY")]
    [InlineData("ConnectionString", "CONNECTION_STRING")]
    [InlineData("ApiPort", "API_PORT")]
    [InlineData("AdminHosts", "ADMIN_HOSTS")]
    [InlineData("TrustForwardedHeaders", "TRUST_FORWARDED_HEADERS")]
    [InlineData("KnownProxies", "KNOWN_PROXIES")]
    public void Names_IncludesUnprefixedSnake(string key, string snake) =>
        Assert.Equal(
            [$"Beacon__{key}", $"BEACON_{snake}", snake, key],
            EnvironmentSetting.Names(key));

    [Theory]
    [InlineData("Beacon__SigningKey")]
    [InlineData("BEACON_SIGNING_KEY")]
    [InlineData("SIGNING_KEY")]
    [InlineData("SigningKey")]
    public void Resolve_EachNameAlone_Resolves(string name) =>
        Assert.Equal("v", Resolve("SigningKey", (name, "v")));

    [Fact]
    public void Resolve_FollowsPrecedence()
    {
        (string, string)[] all =
        [
            ("SigningKey", "bare"),
            ("SIGNING_KEY", "snake"),
            ("BEACON_SIGNING_KEY", "prefixed"),
            ("Beacon__SigningKey", "canonical")
        ];

        Assert.Equal("canonical", Resolve("SigningKey", all));
        Assert.Equal("prefixed", Resolve("SigningKey", all[..3]));
        Assert.Equal("snake", Resolve("SigningKey", all[..2]));
        Assert.Equal("bare", Resolve("SigningKey", all[..1]));
    }

    [Fact]
    public void Resolve_SkipsEmptyValues() =>
        Assert.Equal("snake", Resolve("SigningKey", ("Beacon__SigningKey", ""), ("SIGNING_KEY", "snake")));

    [Fact]
    public void Resolve_NothingSet_ReturnsNull() =>
        Assert.Null(Resolve("SigningKey"));

    [Theory]
    [InlineData("Port", "PORT")]
    [InlineData("Urls", "URLS")]
    [InlineData("HttpPorts", "HTTP_PORTS")]
    public void Names_OmitsHostReservedAliases(string key, string reserved)
    {
        var names = EnvironmentSetting.Names(key);
        Assert.DoesNotContain(names, n => n.Equals(reserved, StringComparison.OrdinalIgnoreCase));
        Assert.Equal([$"Beacon__{key}", $"BEACON_{reserved}"], names.Take(2));
    }
}
