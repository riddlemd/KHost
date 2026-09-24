using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace KHost.Common.Discovery;

/// <summary>Finds services on the local network through macOS's own mDNS daemon.</summary>
/// <remarks>Here because any plugin reaching a network device — a Cast receiver, a speaker, a
/// television — hits the same wall on macOS, and the failure does not look like one.
///
/// <para><b>A .NET process on macOS cannot send multicast.</b> A send to 224.0.0.251 fails with
/// <c>EHOSTUNREACH</c> ("No route to host") while unicast to the very same host succeeds a
/// millisecond later; that asymmetry is how the OS reports a local-network denial, and a
/// command-line binary is denied silently rather than being prompted for. Every managed mDNS stack
/// is therefore blind, and they report it as <i>found nothing</i> rather than <i>could not ask</i>
/// — Zeroconf returns an empty list in half a second from a five-second scan. Expect to lose a day
/// to this if you meet it without knowing.</para>
///
/// <para>Bonjour is the system daemon, reached over XPC rather than the wire, so it is unaffected.
/// This is the macOS half only: <see cref="IsSupported"/> is false everywhere else, where a managed
/// stack works and should be used. Zeroconf ships its own Bonjour browser, but only in its iOS and
/// Mac Catalyst targets — inheriting it would force a platform-specific plugin build.</para></remarks>
public static class BonjourBrowser
{
    // dns_sd lives in libSystem on macOS; there is no separate dylib to ship.
    private const string Lib = "libSystem.dylib";

    private const int NoError = 0;

    /// <summary>kDNSServiceFlagsAdd — set when a service appears, clear when it goes away.</summary>
    private const uint FlagAdd = 0x2;

    /// <summary>Whether a browse callback is announcing a service rather than withdrawing one.</summary>
    /// <remarks>The daemon reports departures through the same callback with the flag clear, so
    /// ignoring it records a device that has just left as one that has just arrived.</remarks>
    public static bool IsArrival(uint flags, int errorCode) => errorCode == NoError && (flags & FlagAdd) != 0;

    /// <summary>True only on macOS. Elsewhere, use an ordinary managed mDNS library.</summary>
    public static bool IsSupported => OperatingSystem.IsMacOS();

    /// <summary>One service instance, already resolved to somewhere you can open a socket to.</summary>
    /// <param name="Name">The instance name, which is what a person recognises.</param>
    /// <param name="HostTarget">Its <c>.local</c> hostname, with no trailing dot.</param>
    /// <param name="Address">Null when the name could not be resolved to an address.</param>
    public sealed record Service(string Name, string HostTarget, int Port, IPAddress? Address);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void BrowseReply(
        IntPtr sdRef, uint flags, uint interfaceIndex, int errorCode,
        IntPtr serviceName, IntPtr regType, IntPtr replyDomain, IntPtr context);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ResolveReply(
        IntPtr sdRef, uint flags, uint interfaceIndex, int errorCode,
        IntPtr fullName, IntPtr hostTarget, ushort port, ushort txtLen, IntPtr txtRecord, IntPtr context);

    [DllImport(Lib)]
    private static extern int DNSServiceBrowse(
        out IntPtr sdRef, uint flags, uint interfaceIndex,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string regType,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? domain,
        BrowseReply callBack, IntPtr context);

    [DllImport(Lib)]
    private static extern int DNSServiceResolve(
        out IntPtr sdRef, uint flags, uint interfaceIndex,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string regType,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string domain,
        ResolveReply callBack, IntPtr context);

    [DllImport(Lib)]
    private static extern int DNSServiceRefSockFD(IntPtr sdRef);

    [DllImport(Lib)]
    private static extern int DNSServiceProcessResult(IntPtr sdRef);

    [DllImport(Lib)]
    private static extern void DNSServiceRefDeallocate(IntPtr sdRef);

    /// <summary>Finds every instance of a service type, resolving each to a host and port.</summary>
    /// <param name="regType">A Bonjour registration type, such as <c>_googlecast._tcp</c>.</param>
    public static async Task<IReadOnlyList<Service>> BrowseAsync(
        string regType, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var names = Discover(regType, timeout, cancellationToken);
        var found = new List<Service>(names.Count);

        foreach (var name in names)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Resolve(name, regType, timeout) is not { } resolved) continue;

            // The .local name resolves through the system resolver, which is mDNSResponder again
            // and so is reachable; only the raw multicast path is blocked.
            IPAddress? address = null;
            try
            {
                var addresses = await Dns.GetHostAddressesAsync(resolved.HostTarget, cancellationToken);
                address = Array.Find(addresses, a => a.AddressFamily == AddressFamily.InterNetwork)
                          ?? addresses.FirstOrDefault();
            }
            catch (Exception) { /* a device that answered browse but not DNS is simply skipped below */ }

            found.Add(resolved with { Address = address });
        }

        return found;
    }

    /// <summary>The browse half: which instances exist.</summary>
    private static List<string> Discover(string regType, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var names = new List<string>();

        // Held in a local so the marshalled thunk cannot be collected while the daemon owns it.
        BrowseReply callback = (_, flags, _, error, serviceName, _, _, _) =>
        {
            if (!IsArrival(flags, error)) return;

            if (Utf8(serviceName) is { Length: > 0 } name && !names.Contains(name))
                names.Add(name);
        };

        if (DNSServiceBrowse(out var sdRef, 0, 0, regType, null, callback, IntPtr.Zero) != NoError)
            return names;

        try { Pump(sdRef, timeout, () => false, cancellationToken); }
        finally { DNSServiceRefDeallocate(sdRef); }

        GC.KeepAlive(callback);
        return names;
    }

    /// <summary>The resolve half: where one instance actually is.</summary>
    private static Service? Resolve(string name, string regType, TimeSpan timeout)
    {
        Service? service = null;

        ResolveReply callback = (_, _, _, error, _, hostTarget, port, _, _, _) =>
        {
            if (error != NoError) return;

            // The daemon hands the port back in network order.
            var hostOrder = (ushort)IPAddress.NetworkToHostOrder((short)port);

            if (Utf8(hostTarget) is { Length: > 0 } host)
                service = new Service(name, host.TrimEnd('.'), hostOrder, null);
        };

        if (DNSServiceResolve(out var sdRef, 0, 0, name, regType, "local.", callback, IntPtr.Zero) != NoError)
            return null;

        try
        {
            // One answer is all a resolve needs, so stop as soon as it lands rather than waiting out
            // the timeout for every device in the room.
            Pump(sdRef, timeout, () => service is not null, CancellationToken.None);
        }
        finally { DNSServiceRefDeallocate(sdRef); }

        GC.KeepAlive(callback);
        return service;
    }

    /// <summary>Drives the daemon's socket until it goes quiet, the caller is satisfied, or time runs out.</summary>
    private static void Pump(IntPtr sdRef, TimeSpan timeout, Func<bool> done, CancellationToken cancellationToken)
    {
        var fd = DNSServiceRefSockFD(sdRef);
        if (fd < 0) return;

        // ownsHandle: false — the handle belongs to the daemon, and DNSServiceRefDeallocate closes it.
        using var socket = new Socket(new SafeSocketHandle((IntPtr)fd, ownsHandle: false));

        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested && !done())
        {
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero) break;

            // Short waits so cancellation and the done() predicate are honoured promptly.
            var slice = remaining < TimeSpan.FromMilliseconds(250) ? remaining : TimeSpan.FromMilliseconds(250);

            if (!socket.Poll(slice, SelectMode.SelectRead)) continue;
            if (DNSServiceProcessResult(sdRef) != NoError) break;
        }
    }

    private static string? Utf8(IntPtr pointer) => pointer == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(pointer);
}
