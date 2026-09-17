namespace KHost.Abstractions.Models.Plugins;

/// <summary>Declares a plugin can offer a QR code; registers via RegisterQrCodeAsync.</summary>
/// <remarks>Read from the manifest without resolving the plugin, so sources list cold.</remarks>
public class PluginQrCodeDefinition
{
    /// <summary>What the venue's source list calls it; blank falls back to the plugin's name.</summary>
    public string? Label { get; set; }
}
