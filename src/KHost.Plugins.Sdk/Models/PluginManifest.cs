namespace KHost.Plugins.Sdk.Models;

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
}
