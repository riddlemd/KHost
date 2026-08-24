using System.Diagnostics;

namespace KHost.UserInterface.Services;

public sealed class ExternalLinkService : IExternalLinkService
{
    // A plain <a target="_blank"> is unreliable inside Photino's webview — there is no host
    // registered to turn a new-window request into a real OS window, so it can just navigate the
    // shell in place instead of leaving it. UseShellExecute hands the URL to the OS's own handler
    // (ShellExecuteEx on Windows, `open` on macOS, `xdg-open` on Linux) — host and browser run on
    // the same machine, so this reaches the desktop's default browser from windowed or headless
    // mode alike.
    public void Open(string url)
        => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
