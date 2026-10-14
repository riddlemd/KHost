using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace KHost.UserInterface.Components;

/// <summary>The settings menu's display switcher: every device every transport knows about, and the
/// search that finds more.</summary>
public partial class DisplayFlyout
{
    [Inject] private IDisplaySwitcher Switcher { get; set; } = default!;
    [Inject] private IFlashService Flash { get; set; } = default!;
    [Inject] private ILogger<DisplayFlyout> Logger { get; set; } = default!;

    [Parameter] public IReadOnlyList<IDisplayProvider> Displays { get; set; } = [];

    /// <summary>The one display carrying the song, as the menu already worked it out for its own row.</summary>
    [Parameter] public IDisplayProvider? LiveDisplay { get; set; }

    /// <summary>Raised once a choice is made, so the menu can close.</summary>
    [Parameter] public EventCallback Done { get; set; }

    /// <summary>Raised when a search starts or stops, so the menu redraws and re-places this flyout.</summary>
    [Parameter] public EventCallback Changed { get; set; }

    /// <summary>True while any transport is actually sweeping — asked of the providers, never
    /// tracked here: discovery outlives the call that started it, so a flag of our own would say
    /// "searching" for five seconds and then lie for the rest of the night.</summary>
    internal bool IsSearching => Displays.Any(display => display.SearchesForDevices && display.IsDiscovering);

    /// <summary>Nothing to offer when every transport opens its own device rather than finding one.</summary>
    internal bool CanSearch => Displays.Any(display => display.SearchesForDevices);

    /// <summary>Names the transport under the device, so two rooms called "TV" are still telling.</summary>
    internal static string DescribeDisplay(IDisplayProvider provider, DisplayDevice device)
    {
        var model = string.IsNullOrWhiteSpace(device.Model) ? provider.Name : device.Model;

        return string.IsNullOrWhiteSpace(device.Address) ? model : $"{model} · {device.Address}";
    }

    internal static bool IsLive(IDisplayProvider provider, DisplayDevice device)
        => provider.ConnectedDeviceId == device.Id;

    /// <summary>Every device every transport knows about, live one included — unlike a split
    /// button's primary half, a checked row in a list is the ordinary way to show the current
    /// choice, and it sits beside the others so switching back is one press.</summary>
    internal IEnumerable<(IDisplayProvider Provider, DisplayDevice Device)> AvailableDisplays()
    {
        foreach (var provider in Displays)
        {
            IReadOnlyList<DisplayDevice> devices;

            // A provider mid-sweep can throw here, and one bad plugin must not empty the list.
            try { devices = provider.Devices; }
            catch { continue; }

            foreach (var device in devices)
                yield return (provider, device);
        }
    }

    private async Task SelectAsync(IDisplayProvider provider, DisplayDevice device)
    {
        // Otherwise an unreachable receiver looks like a press that did nothing.
        if (!await Switcher.SelectAsync(provider, device))
            Flash.Show($"Could not connect to {device.Name}. Check it is on and on the same network.", FlashType.Warning);

        await Done.InvokeAsync();
    }

    private async Task TurnOffAsync()
    {
        await Switcher.TurnOffAsync();

        await Done.InvokeAsync();
    }

    /// <summary>One control for both halves, because the answer is one piece of state: a sweep is
    /// either running or it is not. Stopping matters — a console runs all night on whatever wifi
    /// the room has, and browsing is off until someone asks for it.</summary>
    private async Task ToggleSearchAsync()
    {
        var searching = IsSearching;
        Exception? startFailure = null;

        foreach (var provider in Displays)
        {
            // The screens are opened, not found: asking them to discover launches a screen, which
            // is not what pressing "search" asked for.
            if (!provider.SearchesForDevices) continue;

            if (searching)
                await SafelyAsync(() => provider.StopDiscoveryAsync(), $"Stopping {provider.Name} discovery");
            else
                startFailure ??= await SafelyAsync(() => provider.StartDiscoveryAsync(), $"Starting {provider.Name} discovery");
        }

        // One line however many transports failed; an empty list alone reads as an empty room.
        if (startFailure is not null)
            Flash.Show($"Could not search for devices: {startFailure.Message}", FlashType.Warning);

        // Deliberately left open: a sweep fills the list underneath, and closing the menu would
        // hide the very thing that was asked for.
        await Changed.InvokeAsync();
    }

    /// <summary>A transport that throws must not take the menu down with it.</summary>
    /// <returns>What was thrown, or null.</returns>
    private async Task<Exception?> SafelyAsync(Func<Task> action, string what)
    {
        try
        {
            await action();
            return null;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "{What} failed", what);
            return ex;
        }
    }
}
