using System.Net;

namespace KHost.UserInterface.Http;

/// <summary>The UI has no auth of its own, so only /media and /ipc/screen answer off-box.</summary>
/// <remarks>Everything else stays on this machine.</remarks>
internal static class LanAccessPolicy
{
    private static readonly string[] ReachableOffBox = ["/media", "/ipc/screen"];
    private static readonly string[] LoopbackHosts = ["localhost", "127.0.0.1", "[::1]", "::1"];

    internal static bool IsAllowed(IPAddress? remote, HostString host, PathString path)
        => IsLocal(remote, host) || IsMachineFacing(path);

    /// <summary>A screen or a player device, never a browser, hits these: a redirect isn't followed.</summary>
    /// <remarks>Host is unchecked here: a real device sends the host's LAN IP, not a rebind.</remarks>
    internal static bool IsMachineFacing(PathString path)
        => ReachableOffBox.Any(p => path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase));

    /// <summary>Null means in-process, trusted; IPv4-mapped addresses unwrap for IsLoopback's v4.</summary>
    /// <remarks>Host must also be loopback, since DNS rebinding can repoint a hostname at it.</remarks>
    private static bool IsLocal(IPAddress? remote, HostString host)
    {
        if (remote is null) return true;

        var address = remote.IsIPv4MappedToIPv6 ? remote.MapToIPv4() : remote;
        return IPAddress.IsLoopback(address) && IsLoopbackHost(host);
    }

    private static bool IsLoopbackHost(HostString host)
        => LoopbackHosts.Contains(host.Host, StringComparer.OrdinalIgnoreCase);
}
