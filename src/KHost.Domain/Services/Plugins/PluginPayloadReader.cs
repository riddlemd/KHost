using KHost.Abstractions.Models.Plugins;
using KHost.Abstractions.Services;
using KHost.Plugins.Sdk;
using KHost.Plugins.Sdk.Models;
using System.IO.Compression;
using System.Text.Json;

namespace KHost.Domain.Services.Plugins;

/// <summary>
/// The rules a plugin zip has to satisfy. Shared by the installer and the catalog-sync tool: the
/// tool exists to reject a release the host would refuse, so the two must not drift.
/// </summary>
public class PluginPayloadReader : IPluginPayloadReader
{
    /// <summary>Bounds a zip that unpacks to fill the disk.</summary>
    public const long MaxExpandedBytes = 256L * 1024 * 1024;

    public PluginPayloadContents Unpack(string zipPath, string destination, Guid? expectedId = null)
    {
        Extract(zipPath, destination);

        var root = FindManifestRoot(destination)
            ?? throw new InvalidOperationException($"The download contains no {PluginLoader.ManifestFileName}.");

        return new PluginPayloadContents { Root = root, Manifest = Validate(root, expectedId) };
    }

    private static void Extract(string zipPath, string destination)
    {
        Directory.CreateDirectory(destination);

        var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;

        using var archive = ZipFile.OpenRead(zipPath);

        long expanded = 0;

        // Every entry is checked before a byte is written: a zip that escapes its destination or
        // expands past the cap must not leave half its contents on disk.
        foreach (var entry in archive.Entries)
        {
            expanded += entry.Length;

            if (expanded > MaxExpandedBytes)
                throw new InvalidOperationException("The download expands to more than this host will accept.");

            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
                continue;

            if (!Path.GetFullPath(Path.Combine(destination, entry.FullName)).StartsWith(root, StringComparison.Ordinal))
                throw new InvalidOperationException($"The download writes outside its folder ('{entry.FullName}').");
        }

        archive.ExtractToDirectory(destination);
    }

    /// <summary>A release zip commonly wraps its contents in one folder named for the tag.</summary>
    private static string? FindManifestRoot(string extracted)
    {
        if (File.Exists(Path.Combine(extracted, PluginLoader.ManifestFileName)))
            return extracted;

        var children = Directory.GetDirectories(extracted);

        return children.Length == 1 && File.Exists(Path.Combine(children[0], PluginLoader.ManifestFileName))
            ? children[0]
            : null;
    }

    private static PluginManifest Validate(string root, Guid? expectedId)
    {
        var manifestPath = Path.Combine(root, PluginLoader.ManifestFileName);

        var manifest = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(manifestPath), JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException($"{PluginLoader.ManifestFileName} is empty.");

        if (expectedId is { } expected && manifest.Id != expected)
            throw new InvalidOperationException($"The download declares plugin id {manifest.Id}, but the catalog lists {expected}.");

        if (manifest.ApiVersion != PluginApi.CurrentVersion)
            throw new InvalidOperationException($"Requires plugin API v{manifest.ApiVersion}; this host supports v{PluginApi.CurrentVersion}.");

        // The manifest came off the network and the loader hands EntryAssembly straight to
        // LoadFromAssemblyPath, so a traversing name would load an assembly from outside the folder.
        var entryPath = Path.GetFullPath(Path.Combine(root, manifest.EntryAssembly));

        if (!entryPath.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException($"Entry assembly '{manifest.EntryAssembly}' points outside the plugin folder.");

        if (!File.Exists(entryPath))
            throw new InvalidOperationException($"Entry assembly '{manifest.EntryAssembly}' is missing from the download.");

        return manifest;
    }
}
