using KHost.Abstractions.Models;
using KHost.Abstractions.Models.Plugins;

namespace KHost.Abstractions.Models.Plugins;

/// <summary>An unpacked, validated plugin payload: where its files landed and what it declares.</summary>
public sealed record PluginPayloadContents
{
    /// <summary>The folder holding manifest.json — the extraction root, or the single folder a
    /// release zip wrapped everything in.</summary>
    public required string Root { get; init; }

    public required PluginManifest Manifest { get; init; }
}
