using System.Net;

namespace KHost.UserInterface.Http;

/// <summary>
/// The host binds every interface so a Cast receiver or an off-machine screen can pull the stream,
/// but the UI has no authentication of its own. Only the two paths those consumers need are
/// answered off-box; everything else — queue, library, venue settings — stays on this machine.
/// </summary>
internal static class LanAccessPolicy
{
    private static readonly string[] ReachableOffBox = ["/media", "/ipc/screen"];

    internal static bool IsAllowed(IPAddress? remote, PathString path)
        => IsLocal(remote) || ReachableOffBox.Any(p => path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// A null address means the request never crossed a socket (in-process), so it is as local as
    /// it gets. IPv4-mapped addresses are unwrapped first: ::ffff:127.0.0.1 is loopback, but
    /// IPAddress.IsLoopback only says so once it is back in v4 form.
    /// </summary>
    private static bool IsLocal(IPAddress? remote)
    {
        if (remote is null) return true;

        var address = remote.IsIPv4MappedToIPv6 ? remote.MapToIPv4() : remote;
        return IPAddress.IsLoopback(address);
    }
}
