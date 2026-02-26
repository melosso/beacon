namespace Beacon.Core.Services;

/// <summary>
/// Read-only instance-level options sourced from appsettings / environment variables.
/// These cannot be changed at runtime through the admin UI.
/// </summary>
public sealed class InstanceOptions
{
    /// <summary>
    /// When true, all email notification features are permanently disabled for this instance.
    /// Useful for private or demo deployments. Set via Beacon:DisableEmailNotifications.
    /// </summary>
    public bool DisableEmailNotifications { get; init; } = false;

    /// <summary>
    /// The public-facing base URL of this instance (e.g. "https://consent.example.com").
    /// Used when building absolute confirmation URLs in emails. When set, this takes
    /// precedence over the URL derived from the incoming HTTP request, which is unreliable
    /// behind TLS-terminating reverse proxies that do not forward X-Forwarded-Proto/Host.
    /// Set via Beacon:PublicUrl.
    /// </summary>
    public string? PublicUrl { get; init; }
}
