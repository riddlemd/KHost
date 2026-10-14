using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace KHost.UserInterface.Services;

internal sealed class DisplaySwitcher : IDisplaySwitcher
{
    private readonly ILogger<DisplaySwitcher> _logger;
    private readonly IReadOnlyList<IDisplayProvider> _displays;

    public DisplaySwitcher(ILogger<DisplaySwitcher> logger, IEnumerable<IDisplayProvider> displays)
    {
        _logger = logger;
        _displays = [.. displays];
    }

    public async Task<bool> SelectAsync(IDisplayProvider provider, DisplayDevice device)
    {
        // Already there: a press on the checked row should not tear the song down and rebuild it.
        if (provider.ConnectedDeviceId == device.Id) return true;

        // Off first, or a connect that fails below leaves the song on two.
        await DisconnectOthersAsync(except: provider);

        try
        {
            var connected = await provider.ConnectAsync(device.Id);

            if (!connected)
                _logger.LogWarning("{Provider} could not connect to '{Device}'", provider.Name, device.Name);

            return connected;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Provider} failed connecting to '{Device}'", provider.Name, device.Name);
            return false;
        }
    }

    public Task TurnOffAsync() => DisconnectOthersAsync(except: null);

    public async Task OpenLocalScreenAsync()
    {
        // The screens are the transport the host opens itself rather than finds.
        var screens = _displays.FirstOrDefault(display => !display.SearchesForDevices);
        if (screens?.Devices.FirstOrDefault() is not { } device)
        {
            _logger.LogWarning("No local display is registered, so no screen can be opened");
            return;
        }

        await DisconnectOthersAsync(except: screens);

        // Not through SelectAsync: an already-open screen is still asked, so the provider's own
        // one-screen refusal covers this path too.
        try { await screens.ConnectAsync(device.Id); }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not open a screen"); }
    }

    /// <summary>Every connected provider besides <paramref name="except"/> lets go of the song, in
    /// registration order; one that throws is logged and the rest still let go.</summary>
    private async Task DisconnectOthersAsync(IDisplayProvider? except)
    {
        foreach (var display in _displays)
        {
            if (display == except) continue;
            if (display.ConnectedDeviceId is not { Length: > 0 }) continue;

            try { await display.DisconnectAsync(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Could not disconnect {Provider}", display.Name); }
        }
    }
}
