namespace KHost.Abstractions.Models.Plugins;

public class PluginManifest
{
    public required Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Version { get; set; }
    public string? Author { get; set; }
    public string? Description { get; set; }
    public required string EntryAssembly { get; set; }
    public required int ApiVersion { get; set; }
    /// <summary>A Bootstrap Icons glyph, or <see cref="PluginIcon.ImageSpecifier"/> for its image.</summary>
    public string? Icon { get; set; }

    public List<PluginSettingDefinition> Settings { get; set; } = [];

    /// <summary>Buttons the host draws, run through <see cref="Services.IPluginButtonHandler"/>.</summary>
    public List<PluginButtonDefinition> Buttons { get; set; } = [];

    /// <summary>Extra extensions the folder scan recognises; it asserts, it does not convert.</summary>
    public List<string> ImportFormats { get; set; } = [];

    /// <summary>Lists this plugin as a QR code source; nothing draws until the venue picks it.</summary>
    public PluginQrCodeDefinition? QrCode { get; set; }
}
