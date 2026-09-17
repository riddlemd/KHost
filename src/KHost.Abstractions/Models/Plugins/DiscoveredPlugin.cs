using KHost.Abstractions.Models;
using KHost.Abstractions.Models.Plugins;

namespace KHost.Abstractions.Models.Plugins;

public class DiscoveredPlugin
{
    /// <summary>Absolute path of the plugin's folder under plugins/.</summary>
    public required string Directory { get; init; }

    /// <summary>Null when the manifest was missing or failed to parse.</summary>
    public PluginManifest? Manifest { get; init; }

    public PluginStatus Status { get; set; }

    public string? Error { get; set; }

    public List<string> Warnings { get; } = [];

    /// <summary>Set only when the plugin shipped a usable icon; false degrades to a glyph.</summary>
    public bool HasIconImage { get; set; }

    /// <summary>What the plugin registered on load; empty until loaded, unscanned when disabled.</summary>
    public List<string> Capabilities { get; } = [];

    // "D"-format GUID string; broken manifests fall back to the folder name so the
    // Plugins page still has a stable key to render them under.
    public string Id => Manifest?.Id.ToString() ?? Path.GetFileName(Directory);

    public string DisplayName => Manifest?.Name ?? Path.GetFileName(Directory);
}
