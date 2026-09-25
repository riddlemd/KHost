using System.Net;

namespace KHost.IPC.SignalR;

/// <summary>The host advertises its stream on loopback.</summary>
/// <remarks>A remote screen gets the address it actually reached the host on, not a loopback one.</remarks>
internal static class StreamUrlRewriter
{
    /// <summary>Null means a stems-only load, which has no stream to rewrite; the null carries
    /// straight through.</summary>
    internal static string? ForScreen(string? streamUrl, string? hostAddress)
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
