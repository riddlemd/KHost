using System.Diagnostics;

namespace KHost.UserInterface.Services;

public sealed class ExternalLinkService : IExternalLinkService
{
    // The OS's browser, not a page in the app: in the console window a new-window request would
    // otherwise open a second app window, so UseShellExecute hands off instead.
    public void Open(string url)
        => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
