using System.Buffers.Binary;
using KHost.Abstractions.Models.Plugins;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Abstractions.Services.QueueRotation;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using System.Text.Json;

namespace KHost.Domain.Services.Plugins;

/// <summary>
/// Runs before the container is built, so it cannot use DI services (including ICacheService
/// and ILogger) — state is read straight from disk and failures land on DiscoveredPlugin.
/// A failing plugin never stops the app from starting.
/// </summary>
public static class PluginLoader
{
    public const string ManifestFileName = "manifest.json";

    /// <summary>Interfaces a plugin assembly is scanned for; implementations are registered as
    /// singletons. The label is what the Plugins page shows a host the plugin provides.</summary>
    private static readonly (Type Interface, string Capability)[] ExtensionInterfaces =
    [
        (typeof(IMediaProvider), "Media provider"),
        (typeof(IQueueRotationMode), "Queue rotation"),
        (typeof(IBreakMusicProvider), "Break music"),
    ];

    public static PluginsState ReadState(string cacheDirectory)
    {
        var filePath = Path.Combine(cacheDirectory, "plugins.json");

        try
        {
            if (!File.Exists(filePath))
                return new PluginsState();

            return JsonSerializer.Deserialize<PluginsState>(File.ReadAllText(filePath), JsonSerializerOptions.Web) ?? new PluginsState();
        }
        catch (JsonException)
        {
            // A corrupt state file means every plugin shows as Disabled rather than the app dying.
            return new PluginsState();
        }
    }

    public static List<DiscoveredPlugin> Discover(string pluginsDirectory, PluginsState state)
    {
        var plugins = new List<DiscoveredPlugin>();

        if (!Directory.Exists(pluginsDirectory))
            return plugins;

        var seenIds = new HashSet<Guid>();

        foreach (var directory in Directory.GetDirectories(pluginsDirectory).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            var plugin = DiscoverOne(directory, state, seenIds);

            plugins.Add(plugin);
        }

        return plugins;
    }

    public static void LoadAndRegister(IServiceCollection services, IEnumerable<DiscoveredPlugin> plugins, PluginsState state)
    {
        foreach (var plugin in plugins.Where(p => p.Status == PluginStatus.Enabled))
        {
            try
            {
                LoadOne(services, plugin, state);

                plugin.Status = PluginStatus.Loaded;
            }
            catch (Exception ex)
            {
                plugin.Status = PluginStatus.Errored;
                plugin.Error = ex.Message;
            }
        }
    }

    private static DiscoveredPlugin DiscoverOne(string directory, PluginsState state, HashSet<Guid> seenIds)
    {
        var manifestPath = Path.Combine(directory, ManifestFileName);

        if (!File.Exists(manifestPath))
            return Errored(directory, null, $"No {ManifestFileName} found.");

        PluginManifest manifest;

        try
        {
            manifest = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(manifestPath), JsonSerializerOptions.Web)
                ?? throw new JsonException("Manifest is empty.");
        }
        catch (JsonException ex)
        {
            return Errored(directory, null, $"Invalid {ManifestFileName}: {ex.Message}");
        }

        if (!seenIds.Add(manifest.Id))
            return Errored(directory, manifest, $"Duplicate plugin id '{manifest.Id}'.");

        if (manifest.ApiVersion != PluginApi.CurrentVersion)
        {
            return new DiscoveredPlugin
            {
                Directory = directory,
                Manifest = manifest,
                Status = PluginStatus.Incompatible,
                Error = $"Requires plugin API v{manifest.ApiVersion}; this host supports v{PluginApi.CurrentVersion}.",
            };
        }

        if (!File.Exists(Path.Combine(directory, manifest.EntryAssembly)))
            return Errored(directory, manifest, $"Entry assembly '{manifest.EntryAssembly}' not found.");

        var plugin = new DiscoveredPlugin
        {
            Directory = directory,
            Manifest = manifest,
            Status = state.EnabledPluginIds.Contains(manifest.Id.ToString(), StringComparer.OrdinalIgnoreCase)
                ? PluginStatus.Enabled
                : PluginStatus.Disabled,
        };

        ApplyIcon(plugin, directory, manifest);

        return plugin;
    }

    /// <summary>
    /// Settles whether a plugin's own image can be drawn, here rather than at render: a row that
    /// asked for an <c>img</c> and got a 404 shows a broken picture, where a glyph is a clean
    /// fallback. A plugin that asked for an image and shipped a bad one is told so in Warnings —
    /// silently substituting the glyph would leave the author with nothing to go on.
    /// </summary>
    private static void ApplyIcon(DiscoveredPlugin plugin, string directory, PluginManifest manifest)
    {
        if (!string.Equals(manifest.Icon, PluginIcon.ImageSpecifier, StringComparison.OrdinalIgnoreCase))
            return;

        // Joined to the plugin's own folder from a fixed name, so the manifest cannot steer it.
        var path = Path.Combine(directory, PluginIcon.FileName);

        if (!File.Exists(path))
        {
            plugin.Warnings.Add($"Manifest asks for an image icon but {PluginIcon.FileName} is missing.");
            return;
        }

        if (!TryReadPngSize(path, out var width, out var height))
        {
            plugin.Warnings.Add($"{PluginIcon.FileName} is not a PNG.");
            return;
        }

        if (width > PluginIcon.MaxDimension || height > PluginIcon.MaxDimension)
        {
            plugin.Warnings.Add(
                $"{PluginIcon.FileName} is {width}x{height}; the limit is {PluginIcon.MaxDimension}x{PluginIcon.MaxDimension}.");
            return;
        }

        plugin.HasIconImage = true;
    }

    /// <summary>
    /// Reads the dimensions out of a PNG's IHDR, which is at a fixed offset right after the
    /// signature. Enough to size-check an icon without taking an imaging dependency for it, and it
    /// doubles as the format check — anything that is not a PNG fails the signature.
    /// </summary>
    private static bool TryReadPngSize(string path, out int width, out int height)
    {
        width = height = 0;

        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        Span<byte> header = stackalloc byte[24];

        try
        {
            using var file = File.OpenRead(path);

            if (file.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) < header.Length)
                return false;
        }
        catch (IOException)
        {
            return false;
        }

        if (!header[..8].SequenceEqual(signature) || !header[12..16].SequenceEqual("IHDR"u8))
            return false;

        width = BinaryPrimitives.ReadInt32BigEndian(header[16..20]);
        height = BinaryPrimitives.ReadInt32BigEndian(header[20..24]);

        return width > 0 && height > 0;
    }

    private static void LoadOne(IServiceCollection services, DiscoveredPlugin plugin, PluginsState state)
    {
        var manifest = plugin.Manifest!;
        var entryPath = Path.Combine(plugin.Directory, manifest.EntryAssembly);
        var context = new PluginLoadContext(manifest.Id.ToString(), entryPath);
        var assembly = context.LoadFromAssemblyPath(entryPath);

        var assemblyVersion = assembly.GetName().Version;
        if (assemblyVersion is not null && Version.TryParse(manifest.Version, out var manifestVersion)
            && Normalize(assemblyVersion) != Normalize(manifestVersion))
        {
            plugin.Warnings.Add($"Manifest version {manifestVersion} differs from assembly version {assemblyVersion}.");
        }

        Type[] types;

        try
        {
            types = assembly.GetExportedTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            throw new InvalidOperationException(
                $"Could not scan '{manifest.EntryAssembly}': {ex.LoaderExceptions.FirstOrDefault()?.Message ?? ex.Message}");
        }

        var registered = 0;
        var storedValues = state.Settings.GetValueOrDefault(manifest.Id.ToString());

        foreach (var (extensionInterface, capability) in ExtensionInterfaces)
        {
            foreach (var type in types.Where(t => t.IsClass && !t.IsAbstract && extensionInterface.IsAssignableFrom(t)))
            {
                var implementationType = type;

                services.AddSingleton(extensionInterface, serviceProvider => ActivatorUtilities.CreateInstance(
                    serviceProvider,
                    implementationType,
                    new PluginContext(manifest, storedValues, plugin, serviceProvider.GetRequiredService<IPluginLibrary>())));

                if (!plugin.Capabilities.Contains(capability))
                    plugin.Capabilities.Add(capability);

                registered++;
            }
        }

        // Optional: a plugin that only exposes providers needs no entry point, and loads as before.
        foreach (var type in types.Where(t => t.IsClass && !t.IsAbstract && typeof(IPlugin).IsAssignableFrom(t)))
        {
            var entryPointType = type;

            services.AddSingleton<LoadedPlugin>(serviceProvider => new LoadedPlugin(
                plugin,
                (IPlugin)ActivatorUtilities.CreateInstance(serviceProvider, entryPointType),
                new PluginContext(manifest, storedValues, plugin, serviceProvider.GetRequiredService<IPluginLibrary>())));

            registered++;
        }

        if (registered == 0)
            plugin.Warnings.Add("No extension implementations found in the entry assembly.");
    }

    // "1.0.0" must equal an assembly's 1.0.0.0 — Version treats absent components as -1.
    private static Version Normalize(Version version)
        => new(version.Major, version.Minor, Math.Max(version.Build, 0), Math.Max(version.Revision, 0));

    private static DiscoveredPlugin Errored(string directory, PluginManifest? manifest, string error) => new()
    {
        Directory = directory,
        Manifest = manifest,
        Status = PluginStatus.Errored,
        Error = error,
    };
}
