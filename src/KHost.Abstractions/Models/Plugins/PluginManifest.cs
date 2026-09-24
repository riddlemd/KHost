namespace KHost.Abstractions.Models.Plugins;

/// <summary>A plugin's own declaration of itself: identity, entry point, and what it offers the
/// host. Ships as <c>manifest.json</c> beside the plugin's assemblies.</summary>
public class PluginManifest
{
    /// <summary>The plugin's own identity. Stays the same across every version the plugin ever
    /// publishes; a catalog release and an installed row are recognised as one plugin by this id.</summary>
    public required Guid Id { get; set; }

    /// <summary>Shown throughout the host — the Plugins page, install progress, table dialogs.</summary>
    public required string Name { get; set; }

    /// <summary>The plugin's own version string; shown to the host and otherwise not interpreted.</summary>
    public required string Version { get; set; }

    /// <summary>Publisher's name, shown on the Plugins page. Null when the publisher gave none.</summary>
    public string? Author { get; set; }

    /// <summary>Publisher's description, shown on the Plugins page. Null when the publisher gave
    /// none.</summary>
    public string? Description { get; set; }

    /// <summary>Path to the plugin's entry point assembly, relative to this manifest's own folder.</summary>
    public required string EntryAssembly { get; set; }

    /// <summary>The <see cref="PluginApi.CurrentVersion"/> this plugin was built against. A value
    /// that does not match the host's exactly is refused before load.</summary>
    public required int ApiVersion { get; set; }
    /// <summary>A Bootstrap Icons glyph, or <see cref="PluginIcon.ImageSpecifier"/> for its image.</summary>
    public string? Icon { get; set; }

    /// <summary>Settings the host prompts for on the Plugins page and hands back through
    /// <see cref="Services.IPluginContext"/>.</summary>
    public List<PluginSettingDefinition> Settings { get; set; } = [];

    /// <summary>Buttons the host draws, run through <see cref="Services.IPluginButtonHandler"/>.</summary>
    public List<PluginButtonDefinition> Buttons { get; set; } = [];

    /// <summary>Extra extensions the folder scan recognises; it asserts, it does not convert.</summary>
    public List<string> ImportFormats { get; set; } = [];

    /// <summary>Lists this plugin as a QR code source; nothing draws until the venue picks it.</summary>
    public PluginQrCodeDefinition? QrCode { get; set; }
}
