using KHost.Abstractions.Models;
using KHost.Abstractions.Models.Plugins;

namespace KHost.Abstractions.Models.Plugins;

/// <summary>One plugin found on disk, for the Plugins page — whether or not it loaded.</summary>
public class DiscoveredPlugin
{
    /// <summary>Absolute path of the plugin's folder under plugins/.</summary>
    public required string Directory { get; init; }

    /// <summary>Null when the manifest was missing or failed to parse.</summary>
    public PluginManifest? Manifest { get; init; }

    /// <summary>How far this plugin got: discovered, enabled, loaded, or refused and why.</summary>
    public PluginStatus Status { get; set; }

    /// <summary>Why loading failed, for display; null unless <see cref="Status"/> is <see
    /// cref="PluginStatus.Errored"/> or <see cref="PluginStatus.Incompatible"/>.</summary>
    public string? Error { get; set; }

    /// <summary>Non-fatal problems noticed while loading; the plugin still runs.</summary>
    public List<string> Warnings { get; } = [];

    /// <summary>Set only when the plugin shipped a usable icon; false degrades to a glyph.</summary>
    public bool HasIconImage { get; set; }

    /// <summary>What the plugin registered on load; empty until loaded, unscanned when disabled.</summary>
    public List<string> Capabilities { get; } = [];

    // "D"-format GUID string; broken manifests fall back to the folder name so the
    // Plugins page still has a stable key to render them under.
    /// <summary>The manifest's id, or the folder name when there is no manifest to read one from.</summary>
    public string Id => Manifest?.Id.ToString() ?? Path.GetFileName(Directory);

    /// <summary>The manifest's name, or the folder name when there is no manifest to read one from.</summary>
    public string DisplayName => Manifest?.Name ?? Path.GetFileName(Directory);
}
