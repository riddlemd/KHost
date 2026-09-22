namespace KHost.UserInterface.Services;

/// <summary>Opens and closes the host's own screens, wherever the console asks from.</summary>
/// <remarks>Shared rather than inlined where it is pressed: naming the next screen means reading
/// which names are already taken, and two copies of that would eventually disagree and open a
/// second "Screen 1".</remarks>
public interface IScreenLauncher
{
    /// <summary>Opens one, named for the first "Screen n" nothing is using.</summary>
    /// <returns>False when no provider can start a screen here, so a caller can say so instead of
    /// waiting for one that is never coming.</returns>
    Task<bool> LaunchAsync(CancellationToken cancellationToken = default);

    /// <summary>Closes only screens this host started; one somebody ran themselves is left alone.
    /// </summary>
    void CloseSpawnedScreens();
}
