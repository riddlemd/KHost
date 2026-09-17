using System.Diagnostics;

namespace KHost.UserInterface.Services;

public sealed class ExternalLinkService : IExternalLinkService
{
    // A plain <a target="_blank"> is unreliable inside Photino's webview: no host is registered to
    // turn a new-window request into a real OS window, so UseShellExecute hands off instead.
    public void Open(string url)
        => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
