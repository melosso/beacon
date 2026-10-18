namespace Beacon.Core.Services;

/// <summary>
/// Environment variable names accepted for a key of the Beacon configuration section.
/// </summary>
public static class EnvironmentSetting
{
    // Names the .NET host reads itself; an unprefixed alias must not claim them.
    private static readonly HashSet<string> HostReserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "URLS", "PORT", "HTTP_PORTS", "HTTPS_PORTS"
    };

    /// <summary>
    /// Names for <paramref name="key"/> in precedence order: <c>Beacon__SigningKey</c>,
    /// <c>BEACON_SIGNING_KEY</c>, <c>SIGNING_KEY</c>, <c>SigningKey</c>. Unprefixed names the .NET host reads are omitted.
    /// </summary>
    public static IReadOnlyList<string> Names(string key)
    {
        var snake = ToScreamingSnake(key);
        var names = new List<string>(4) { $"Beacon__{key}", $"BEACON_{snake}" };
        if (!IsHostReserved(snake)) names.Add(snake);
        if (!IsHostReserved(key)) names.Add(key);
        return names;
    }

    /// <summary>First non-empty value among <see cref="Names"/>, or null.</summary>
    public static string? Resolve(string key, Func<string, string?> lookup) =>
        Names(key).Select(lookup).FirstOrDefault(value => !string.IsNullOrEmpty(value));

    /// <summary>First non-empty process environment value among <see cref="Names"/>, or null.</summary>
    public static string? Resolve(string key) => Resolve(key, Environment.GetEnvironmentVariable);

    /// <summary>
    /// Copies <c>KEY=value</c> lines from <paramref name="path"/> into the process environment.
    /// Variables already set in the environment win. Blank lines and <c>#</c> comments are skipped.
    /// </summary>
    public static void LoadEnvFile(string path)
    {
        if (!File.Exists(path)) return;

        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (line.StartsWith("export ", StringComparison.Ordinal)) line = line[7..];

            var parts = line.Split('=', 2);
            if (parts.Length != 2) continue;

            var name = parts[0].Trim();
            var value = parts[1].Trim().Trim('"', '\'');
            if (name.Length > 0 && string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name)))
                Environment.SetEnvironmentVariable(name, value);
        }
    }

    private static bool IsHostReserved(string name) =>
        HostReserved.Contains(name) ||
        name.StartsWith("ASPNETCORE_", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("DOTNET_", StringComparison.OrdinalIgnoreCase);

    // "SigningKey" -> "SIGNING_KEY"
    private static string ToScreamingSnake(string name) =>
        string.Concat(name.Select((c, i) => char.IsUpper(c) && i > 0 ? "_" + c : c.ToString())).ToUpperInvariant();
}
