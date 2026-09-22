using KHost.Abstractions.Services;
using KHost.Abstractions.Services.IPC;

namespace KHost.UserInterface.Services;

/// <inheritdoc cref="IScreenLauncher"/>
public sealed class ScreenLauncher(
    IEnumerable<IScreenProvider> providers,
    IScreenServer screenServer) : IScreenLauncher
{
    public async Task<bool> LaunchAsync(CancellationToken cancellationToken = default)
    {
        var provider = providers.FirstOrDefault(p => p.IsAvailable);
        if (provider is null) return false;

        await provider.LaunchAsync(await NextScreenNameAsync(), cancellationToken);

        return true;
    }

    public void CloseSpawnedScreens()
    {
        foreach (var provider in providers)
        {
            try
            {
                provider.CloseSpawnedScreens();
            }
            catch (Exception)
            {
                // One provider failing must not leave the others' screens up.
            }
        }
    }

    /// <summary>The first "Screen n" nothing is already using.</summary>
    private async Task<string> NextScreenNameAsync()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await foreach (var screen in screenServer.GetConnectedScreensAsync())
            taken.Add(screen.ScreenId);

        for (var i = 1; ; i++)
        {
            var name = $"Screen {i}";
            if (!taken.Contains(name)) return name;
        }
    }
}
