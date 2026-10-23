namespace KHost.UserInterface.Services;

/// <summary>Opens a URL in the OS default browser rather than navigating the app's own shell:
/// the console window has no tab strip or address bar to come back from.</summary>
public interface IExternalLinkService
{
    void Open(string url);
}
