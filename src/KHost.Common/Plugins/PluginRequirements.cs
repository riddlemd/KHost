using System.Text.Json;
using KHost.Abstractions.Models.Plugins;

namespace KHost.Common.Plugins;

/// <summary>Which contracts a built plugin needs: the oldest <c>KHost.Abstractions</c> it was
/// compiled against, read from what the build recorded rather than declared by its author.</summary>
public static class PluginRequirements
{
    /// <summary>The <see cref="PluginManifest.ApiVersion"/> every plugin built against
    /// <see cref="ContractsVersion.Legacy"/> declares.</summary>
    public const int LegacyApiVersion = 8;

    private const string AbstractionsLibraryPrefix = "KHost.Abstractions/";

    /// <summary>The <c>KHost.Abstractions</c> version a plugin's <c>.deps.json</c> names. Null when
    /// the document is not JSON, names no <c>KHost.Abstractions</c>, or names one that is not a
    /// contracts version.</summary>
    /// <param name="depsJson">The plugin's dependency manifest, as the .NET SDK writes it beside its
    /// entry assembly. Left open.</param>
    public static ContractsVersion? ReadDepsJson(Stream depsJson)
    {
        try
        {
            using var document = JsonDocument.Parse(depsJson);

            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("libraries", out var libraries)
                || libraries.ValueKind != JsonValueKind.Object)
                return null;

            foreach (var library in libraries.EnumerateObject())
            {
                if (library.Name.StartsWith(AbstractionsLibraryPrefix, StringComparison.OrdinalIgnoreCase))
                    return ContractsVersion.TryParse(library.Name[AbstractionsLibraryPrefix.Length..], out var version) ? version : null;
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The contracts a plugin declaring only <paramref name="apiVersion"/> needs:
    /// <see cref="ContractsVersion.Legacy"/> for <see cref="LegacyApiVersion"/>, null for anything
    /// else (no host reading contracts versions runs it).</summary>
    public static ContractsVersion? FromLegacyApiVersion(int? apiVersion)
        => apiVersion == LegacyApiVersion ? ContractsVersion.Legacy : null;

    /// <summary>Where the plugin in <paramref name="folder"/> keeps its <c>.deps.json</c>: beside its
    /// entry assembly, under the same name.</summary>
    public static string DepsJsonPath(string folder, PluginManifest manifest)
        => Path.Combine(folder, Path.ChangeExtension(manifest.EntryAssembly, ".deps.json"));

    /// <summary>The contracts the plugin unpacked in <paramref name="folder"/> needs: what its
    /// <c>.deps.json</c> names, else what its legacy <see cref="PluginManifest.ApiVersion"/> means.
    /// Null when neither says.</summary>
    public static ContractsVersion? ReadFromFolder(string folder, PluginManifest manifest)
    {
        var path = DepsJsonPath(folder, manifest);
        ContractsVersion? fromDeps = null;

        // A plugin folder is scanned at startup, where an unreadable file must refuse one plugin, not stop the host.
        try
        {
            if (File.Exists(path))
            {
                using var stream = File.OpenRead(path);

                fromDeps = ReadDepsJson(stream);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            fromDeps = null;
        }

        return fromDeps ?? FromLegacyApiVersion(manifest.ApiVersion);
    }

    /// <summary>Why a plugin needing <paramref name="required"/> (null when nothing it ships says)
    /// does not run on a host with <paramref name="host"/>, in words a host can act on. Null when it
    /// runs.</summary>
    /// <param name="host">The contracts the host has.</param>
    /// <param name="required">What the plugin needs, or null when it could not be read.</param>
    /// <param name="legacyApiVersion">The manifest's legacy plugin API, used only to word a refusal
    /// when <paramref name="required"/> is null.</param>
    public static string? DescribeRefusal(this ContractsVersion host, ContractsVersion? required, int? legacyApiVersion)
    {
        if (required is { } version)
            return host.DescribeRefusal(version);

        return legacyApiVersion switch
        {
            null => "Names no contracts version: its .deps.json lists no KHost.Abstractions and its manifest declares no apiVersion.",
            < LegacyApiVersion => $"Built for an older KHost (plugin API {legacyApiVersion}; this KHost has contracts {host}); it needs an update.",
            _ => $"Needs a newer KHost (built for plugin API {legacyApiVersion}; this KHost has contracts {host}).",
        };
    }
}
