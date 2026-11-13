namespace Beacon.Core.Models;

public enum ConsentStatus
{
    OptedIn = 0,
    OptedOut = 1,
    PendingConfirmation = 2  // Awaiting double opt-in email confirmation
}

public enum ConsentSource
{
    Url = 0,
    Api = 1,
    Admin = 2
}

public static class ConsentTransition
{
    /// <summary>Whether a stored status of <paramref name="from"/> accepts <paramref name="to"/>. Pending confirmation never replaces an opt-in.</summary>
    public static bool IsAllowed(ConsentStatus? from, ConsentStatus to) =>
        !(to == ConsentStatus.PendingConfirmation && from == ConsentStatus.OptedIn);
}
