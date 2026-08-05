using System.Net;
using System.Net.Sockets;

namespace Beacon.Security;

/// <summary>
/// Shared guard for outbound requests to operator-supplied URLs. Resolves the host once and returns the
/// validated address so callers can pin the connection to it, closing the DNS-rebinding window between
/// validation and connect.
/// </summary>
public static class SsrfGuard
{
    /// <summary>
    /// Returns the address to connect to, or null when the URL is malformed, uses a non-HTTP scheme,
    /// or resolves to any private, loopback or otherwise reserved address.
    /// </summary>
    public static async Task<IPAddress?> ResolveAndValidateAsync(string? url, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return null;

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return null;

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(uri.Host, ct);
            if (addresses.Length == 0 || addresses.Any(IsPrivateOrReserved))
                return null;

            return addresses[0];
        }
        catch (Exception ex) when (ex is SocketException or ArgumentException or OperationCanceledException)
        {
            return null;
        }
    }

    public static bool IsPrivateOrReserved(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
            return true;

        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] switch
            {
                10 => true,                                          // 10.0.0.0/8
                127 => true,                                         // 127.0.0.0/8
                169 when bytes[1] == 254 => true,                    // 169.254.0.0/16 (link-local, cloud metadata)
                172 when bytes[1] >= 16 && bytes[1] <= 31 => true,   // 172.16.0.0/12
                192 when bytes[1] == 168 => true,                    // 192.168.0.0/16
                0 => true,                                           // 0.0.0.0/8
                100 when bytes[1] >= 64 && bytes[1] <= 127 => true,  // 100.64.0.0/10 (CGNAT)
                _ => false
            };
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6UniqueLocal)
                return true;

            // :: and ::1
            return address.GetAddressBytes().Take(15).All(b => b == 0);
        }

        return false;
    }
}
