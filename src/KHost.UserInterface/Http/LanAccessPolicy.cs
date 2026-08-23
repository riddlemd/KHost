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
    private static readonly string[] LoopbackHosts = ["localhost", "127.0.0.1", "[::1]", "::1"];

    internal static bool IsAllowed(IPAddress? remote, HostString host, PathString path)
        => IsLocal(remote, host) || IsMachineFacing(path);

    /// <summary>
    /// These paths answer a screen or a Cast receiver, never a browser, so no UI-level gate may
    /// intercept them: a redirect reaches something that cannot follow one and reads as a stream.
    /// The Host header is never checked here — a real screen or Cast receiver sends the host's LAN
    /// IP as Host, which is neither loopback nor a rebinding attempt.
    /// </summary>
    internal static bool IsMachineFacing(PathString path)
        => ReachableOffBox.Any(p => path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// A null address means the request never crossed a socket (in-process), so it is as local as
    /// it gets and an empty Host (in-process calls carry none) is fine — that leniency applies only
    /// here, before a real socket is in play. IPv4-mapped addresses are unwrapped first:
    /// ::ffff:127.0.0.1 is loopback, but IPAddress.IsLoopback only says so once it is back in v4
    /// form. For a real socket, a loopback source IP is not enough: DNS rebinding lets a page an
    /// operator is browsing repoint its hostname at 127.0.0.1, so the request arrives with a
    /// loopback RemoteIpAddress but the attacker's Host header. Trust loopback only when Host is
    /// also loopback, so a genuine request (Host: localhost:5251) passes and a rebound one
    /// (Host: attacker.com) does not.
    /// </summary>
    private static bool IsLocal(IPAddress? remote, HostString host)
    {
        if (remote is null) return true;

        var address = remote.IsIPv4MappedToIPv6 ? remote.MapToIPv4() : remote;
        return IPAddress.IsLoopback(address) && IsLoopbackHost(host);
    }

    private static bool IsLoopbackHost(HostString host)
        => LoopbackHosts.Contains(host.Host, StringComparer.OrdinalIgnoreCase);
}
