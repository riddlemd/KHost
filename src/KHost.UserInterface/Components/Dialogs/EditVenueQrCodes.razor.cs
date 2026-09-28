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

    /// <summary>Kept selected when no plugin declares it, which the break music mode also does.</summary>
    private string? UnavailableQrCodeSource
        => string.IsNullOrWhiteSpace(Model.QrCodeSource)
           || QrCodeSources.Any(source => string.Equals(source.Id, Model.QrCodeSource, StringComparison.OrdinalIgnoreCase))
            ? null
            : Model.QrCodeSource;
}
