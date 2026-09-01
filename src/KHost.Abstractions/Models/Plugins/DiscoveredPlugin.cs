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

    /// <summary>
    /// Set when the plugin asked for its own image and shipped a usable one. False leaves the row
    /// on a glyph, so a missing or oversized file degrades rather than drawing a broken image.
    /// </summary>
    public bool HasIconImage { get; set; }

    /// <summary>What the plugin actually registered on load, for display. Empty until it loads —
    /// a disabled plugin's assembly is never scanned, so the host cannot claim it provides anything.</summary>
    public List<string> Capabilities { get; } = [];

    // "D"-format GUID string; broken manifests fall back to the folder name so the
    // Plugins page still has a stable key to render them under.
    public string Id => Manifest?.Id.ToString() ?? Path.GetFileName(Directory);

    public string DisplayName => Manifest?.Name ?? Path.GetFileName(Directory);
}
