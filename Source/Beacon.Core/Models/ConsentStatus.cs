namespace Beacon.Core.Models;

public enum ConsentStatus
{
    OptedIn = 0,
    OptedOut = 1
}

public enum ConsentSource
{
    Url = 0,
    Api = 1,
    Admin = 2
}
