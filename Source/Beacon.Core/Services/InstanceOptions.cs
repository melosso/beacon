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
}
