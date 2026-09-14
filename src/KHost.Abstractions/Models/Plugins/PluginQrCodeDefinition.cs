namespace KHost.Abstractions.Models.Plugins;

/// <summary>
/// Declares that a plugin can put a QR code on the screens, so a venue can choose it before the
/// plugin has one to give. Registering the code itself is
/// <see cref="Services.IPluginContext.RegisterQrCodeAsync"/> — this is only the offer.
/// </summary>
/// <remarks>
/// Read from the manifest without resolving the plugin, the same as <c>ImportFormats</c>: the
/// Venues dialog has to list the source cold. the provider's code exists only once a host has signed
/// in, and a venue that could not pick it until then would have to be set up mid-show.
/// </remarks>
public class PluginQrCodeDefinition
{
    /// <summary>
    /// What the venue's source list calls it — "Guest sign-up", not the plugin's own name, which
    /// the dialog already has. Blank falls back to the plugin's name.
    /// </summary>
    public string? Label { get; set; }
}
