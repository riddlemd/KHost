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
    /// <summary>
    /// A Bootstrap Icons glyph name, or <see cref="PluginIcon.ImageSpecifier"/> to use the
    /// <see cref="PluginIcon.FileName"/> shipped beside this manifest. Optional — a plugin that
    /// says nothing gets a generic glyph.
    /// </summary>
    public string? Icon { get; set; }

    public List<PluginSettingDefinition> Settings { get; set; } = [];

    /// <summary>
    /// Buttons the host draws on this plugin's row, each run through
    /// <see cref="Services.IPluginButtonHandler"/>. Empty for a plugin that has none.
    /// </summary>
    public List<PluginButtonDefinition> Buttons { get; set; } = [];

    /// <summary>
    /// Extra file extensions (e.g. ".khv") this plugin's own output uses that the media importer's
    /// folder scan should recognise. The plugin is asserting KHost can already play these as they
    /// are — this teaches the scanner to stop skipping them, not how to convert anything.
    /// </summary>
    public List<string> ImportFormats { get; set; } = [];
}
