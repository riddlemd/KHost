using Oriel;
using Velopack;

namespace KHost.UserInterface.Services;

/// <summary>Checks the update feed once a run and holds a downloaded update until the operator restarts for it.</summary>
/// <remarks>Only in the window, and only for a Velopack install with a feed configured: a dev run, a loose publish and
/// <c>--headless</c> never check. Installing restarts KHost, which clears the singer queue like any exit, so it waits
/// for the operator's word rather than restarting mid-song.</remarks>
public sealed class AppUpdateService(IConfiguration configuration, ILogger<AppUpdateService> logger) : BackgroundService
{
    /// <summary>The feed: a folder, an http(s) URL or a GitHub repository URL. Empty turns update checks off.</summary>
    public const string FeedUrlKey = "Updates:FeedUrl";

    /// <summary>The version downloaded and ready to install, or null.</summary>
    public string? ReadyVersion { get; private set; }

    /// <summary>Raised when <see cref="ReadyVersion"/> changes.</summary>
    public event Action? Changed;

    /// <summary>Whether this run checks the feed.</summary>
    internal static bool ShouldCheck(string? feedUrl, bool isInstalled) => isInstalled && !string.IsNullOrWhiteSpace(feedUrl);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var feedUrl = configuration[FeedUrlKey];
        var isInstalled = !string.IsNullOrWhiteSpace(feedUrl) && new UpdateManager(feedUrl).IsInstalled;
        if (!ShouldCheck(feedUrl, isInstalled))
        {
            logger.LogInformation("Update checks off: {Reason}",
                string.IsNullOrWhiteSpace(feedUrl) ? $"no {FeedUrlKey}" : "not a Velopack install");
            return;
        }

        var updater = Oriel.AutoUpdater.Default;
        updater.SetFeedUrl(feedUrl!);
        updater.UpdateDownloaded += (_, e) =>
        {
            ReadyVersion = e.ReleaseName;
            logger.LogInformation("Update {Version} downloaded from {Feed}; waiting for a restart", e.ReleaseName, e.UpdateUrl);
            Changed?.Invoke();
        };
        updater.UpdateNotAvailable += (_, _) => logger.LogInformation("No update on {Feed}", feedUrl);

        try
        {
            await updater.CheckForUpdatesAsync();
        }
        catch (Exception ex)
        {
            // A feed that is down or a bad download must not take the host with it.
            logger.LogWarning(ex, "Update check against {Feed} failed", feedUrl);
        }
    }

    /// <summary>Quits KHost, installs the downloaded update and starts the new version.</summary>
    public void RestartToUpdate()
    {
        logger.LogInformation("Restarting to install update {Version}", ReadyVersion);
        Oriel.AutoUpdater.Default.QuitAndInstall();
    }
}
