using KHost.Abstractions.Models;
using KHost.Abstractions.Services;

namespace KHost.UserInterface.Services;

/// <summary>Moves the song between displays, one display at a time across every provider: whatever
/// else holds the song lets go before another display takes it.</summary>
public interface IDisplaySwitcher
{
    /// <summary>Puts the song on <paramref name="device"/>, taking it off every other display first.</summary>
    /// <returns>Whether <paramref name="provider"/> now carries the song. True without touching
    /// anything when it already did; false when it refused or threw.</returns>
    Task<bool> SelectAsync(IDisplayProvider provider, DisplayDevice device);

    /// <summary>Takes the song off every display.</summary>
    Task TurnOffAsync();

    /// <summary>Opens a screen on this machine, taking the song off every other display first. Does
    /// nothing when no transport opens its own device.</summary>
    Task OpenLocalScreenAsync();
}
