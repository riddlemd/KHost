using KHost.Abstractions.Services;

namespace KHost.Domain.Services.Screens;

/// <summary>The one display the song comes out of, with its device once the provider lists one.</summary>
/// <remarks><see cref="Device"/> is null for a provider that knows it is connected before it has
/// anything to list; callers decide for themselves whether unknown counts as capable.</remarks>
internal readonly record struct ConnectedDisplay(IDisplayProvider Provider, DisplayDevice? Device)
{
    /// <summary>Null when nothing is connected.</summary>
    /// <remarks>Searched, because the screens and every plugin transport are registered side by
    /// side; the connection is the authoritative fact, not the device list.</remarks>
    public static ConnectedDisplay? Find(IEnumerable<IDisplayProvider> providers)
    {
        foreach (var provider in providers)
        {
            if (provider.ConnectedDeviceId is not { Length: > 0 } deviceId) continue;

            var device = provider.Devices.FirstOrDefault(d => d.Id == deviceId)
                ?? provider.Devices.FirstOrDefault(d => d.IsConnected);

            return new ConnectedDisplay(provider, device);
        }

        return null;
    }
}
