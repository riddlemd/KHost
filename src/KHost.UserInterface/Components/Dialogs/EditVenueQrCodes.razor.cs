using KHost.Abstractions.Services;
using KHost.UserInterface.Models;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Dialogs;

/// <summary>The venue dialog's "Screen QR codes" section.</summary>
public partial class EditVenueQrCodes
{
    [Parameter, EditorRequired] public EditVenueModel Model { get; set; } = default!;

    [Inject] private IPluginRegistry Plugins { get; set; } = default!;

    /// <summary>Read from manifests, not registrations, so a venue can be set up before the show.</summary>
    private IEnumerable<(string Id, string Label)> QrCodeSources
        => Plugins.Plugins
            .Where(plugin => plugin.Manifest?.QrCode is not null)
            .Select(plugin => (
                plugin.Id,
                Label: string.IsNullOrWhiteSpace(plugin.Manifest!.QrCode!.Label)
                    ? plugin.DisplayName
                    : plugin.Manifest.QrCode.Label!))
            .OrderBy(source => source.Label, StringComparer.CurrentCultureIgnoreCase);

    /// <summary>The venue's stored source, captured once at open, when no loaded plugin declares
    /// it. Reading <see cref="Model"/>'s live value here instead loses the option the moment the
    /// host picks something else — the placeholder would vanish from the select along with it, so
    /// switching back to it saved an empty value rather than the source the venue actually had.
    /// The break music mode has the identical trap.</summary>
    private string? _unavailableQrCodeSource;

    /// <summary>Kept selected when no plugin declares it, which the break music mode also does.</summary>
    private string? UnavailableQrCodeSource => _unavailableQrCodeSource;

    /// <summary>Whether the select is currently sitting on that unavailable source.</summary>
    private bool IsOnUnavailableQrCodeSource
        => _unavailableQrCodeSource is not null
           && string.Equals(Model.QrCodeSource, _unavailableQrCodeSource, StringComparison.OrdinalIgnoreCase);

    protected override void OnInitialized()
    {
        // Snapshot before anything can change it: whether this source is unavailable is a fact
        // about what the venue had on open, not about whatever the select currently shows.
        _unavailableQrCodeSource =
            !string.IsNullOrWhiteSpace(Model.QrCodeSource)
            && !QrCodeSources.Any(source => string.Equals(source.Id, Model.QrCodeSource, StringComparison.OrdinalIgnoreCase))
                ? Model.QrCodeSource
                : null;
    }
}
