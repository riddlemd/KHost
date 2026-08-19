using System.Net;

namespace KHost.IPC.SignalR;

/// <summary>
/// The host advertises its stream on loopback, which is only fetchable by a screen on this machine.
/// A screen elsewhere is handed the host address it actually reached us on, so a box with several
/// interfaces answers on the one that screen can already route to.
/// </summary>
internal static class StreamUrlRewriter
{
    internal static string ForScreen(string streamUrl, string? hostAddress)
    {
        if (string.IsNullOrEmpty(streamUrl) || string.IsNullOrEmpty(hostAddress)) return streamUrl;
        if (!Uri.TryCreate(streamUrl, UriKind.Absolute, out var uri) || !uri.IsLoopback) return streamUrl;

        if (!IPAddress.TryParse(hostAddress, out var address)) return streamUrl;

        var reachable = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

        // A screen on this machine reached us on loopback too, and swapping that for itself would
        // only churn the URL.
        if (IPAddress.IsLoopback(reachable)) return streamUrl;

        return new UriBuilder(uri) { Host = reachable.ToString() }.Uri.ToString();
    }
}
