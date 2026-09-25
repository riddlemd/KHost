namespace KHost.IPC.SignalR.Contracts;

/// <summary>Starts and stops LocalScreen app processes on the host's own machine.</summary>
/// <remarks>A singleton.</remarks>
public interface IScreenProvider
{
    /// <summary>A short name for logs and messages.</summary>
    string Name { get; }

    /// <summary>Whether this provider can launch a screen here, i.e. the screen app is installed.</summary>
    bool IsAvailable { get; }

    /// <summary>Provisions a key for <paramref name="screenId"/> and starts a screen process that
    /// will connect as that id.</summary>
    /// <remarks>Completes once the process has started, not once the screen has connected; watch
    /// <see cref="IScreenServer.ScreenConnected"/> for that.</remarks>
    /// <exception cref="FileNotFoundException">The screen app is not where this provider expects it;
    /// check <see cref="IsAvailable"/> first.</exception>
    Task LaunchAsync(string screenId, CancellationToken cancellationToken = default);

    /// <summary>Closes only screens this provider started; one running elsewhere is left alone.</summary>
    /// <remarks>Also revokes each closed screen's key. Safe to call more than once.</remarks>
    void CloseSpawnedScreens();
}
