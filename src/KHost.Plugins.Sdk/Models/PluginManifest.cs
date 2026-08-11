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
    public List<PluginSettingDefinition> Settings { get; set; } = [];
}
