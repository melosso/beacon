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
