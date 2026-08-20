namespace KHost.Abstractions.Services.IPC;

public interface IScreenProvider
{
    string Name { get; }
    bool IsAvailable { get; }
    Task LaunchAsync(string screenId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Shuts down only the screens this provider started itself. A screen running anywhere but as
    /// our own child process is not ours to close, so it is left alone — quitting the host must not
    /// take down a display someone else is running.
    /// </summary>
    void CloseSpawnedScreens();
}
