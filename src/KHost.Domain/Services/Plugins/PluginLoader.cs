using KHost.Abstractions.Messaging;
using System.Buffers.Binary;
using KHost.Abstractions.Models.Plugins;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Abstractions.Services.QueueRotation;
using KHost.Common.Plugins;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Reflection;
using System.Text.Json;

namespace KHost.Domain.Services.Plugins;

/// <summary>Runs before the container is built: no DI, so failures land on DiscoveredPlugin.</summary>
public static class PluginLoader
{
    public const string ManifestFileName = "manifest.json";

    /// <summary>Every plugin-facing interface the loader binds: each one the contracts mark
    /// <see cref="PluginExtensionPointAttribute"/>, and no other.</summary>
    /// <remarks>Read from the contracts, so a new extension point is bound the moment it is marked;
    /// <c>PluginExtensionInterfaceTests</c> fails on any interface the host collects that is not.</remarks>
    internal static readonly Type[] ExtensionInterfaces = typeof(PluginExtensionPointAttribute).Assembly.GetExportedTypes()
        .Where(type => type.IsInterface && type.GetCustomAttribute<PluginExtensionPointAttribute>() is not null)
        .OrderBy(type => type.FullName, StringComparer.Ordinal)
        .ToArray();

    /// <summary>The extension points the Plugins page names as things a plugin provides to the show,
    /// in the order the row lists them.</summary>
    private static readonly (Type Interface, string Capability)[] CapabilityInterfaces = ExtensionInterfaces
        .Select(type => (Type: type, Point: type.GetCustomAttribute<PluginExtensionPointAttribute>()!))
        .Where(pair => pair.Point.Capability is not null)
        .OrderBy(pair => pair.Point.Order)
        .Select(pair => (pair.Type, pair.Point.Capability!))
        .ToArray();

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
        => LoadAndRegister(services, plugins, state, new PluginSettingsConfiguration(state.Settings));

    public static void LoadAndRegister(IServiceCollection services, IEnumerable<DiscoveredPlugin> plugins, PluginsState state, PluginSettingsConfiguration settings)
    {
        var configuration = new ConfigurationBuilder().Add(settings).Build();

        foreach (var plugin in plugins.Where(p => p.Status == PluginStatus.Enabled))
        {
            try
            {
                LoadOne(services, plugin, settings, configuration);

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

        if (PluginApiRange.ThisHost.DescribeRefusal(manifest.ApiVersion) is { } refusal)
            return Incompatible(directory, manifest, refusal);

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

    /// <summary>Settles whether a plugin image can draw, here rather than at render time.</summary>
    /// <remarks>A bad image is told to the author in Warnings, not silently swapped for a glyph.</remarks>
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

    /// <summary>Reads dimensions from the PNG's IHDR, sizing an icon with no imaging dependency.</summary>
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

    private static void LoadOne(IServiceCollection services, DiscoveredPlugin plugin, PluginSettingsConfiguration settings, IConfiguration configuration)
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
        PluginContext CreateContext(IServiceProvider serviceProvider) => new(manifest, plugin,
            serviceProvider.GetRequiredService<Secrets.IPluginSecretStore>(),
            serviceProvider.GetRequiredService<QrCodes.IQrCodeService>(),
            serviceProvider.GetRequiredService<IMessageBroker>(),
            serviceProvider.GetRequiredService<IFlashService>(),
            serviceProvider.GetRequiredService<ILogger<PluginContext>>());

        var concreteTypes = types.Where(t => t.IsClass && !t.IsAbstract).ToList();

        // One singleton per extension type, every interface pointing at the same instance. Per-interface
        // registration would build two, so signing in on one would not sign in the other.
        var extensionTypes = concreteTypes.Where(t => ExtensionInterfaces.Any(i => i.IsAssignableFrom(t))).ToList();

        foreach (var type in extensionTypes)
        {
            var implementationType = type;

            services.AddSingleton(implementationType, serviceProvider => CreateExtension(
                serviceProvider, implementationType, () => CreateContext(serviceProvider)));

            foreach (var extensionInterface in ExtensionInterfaces.Where(i => i.IsAssignableFrom(implementationType)))
                services.AddSingleton(extensionInterface, sp => sp.GetRequiredService(implementationType));

            // The Plugins page reaches a button handler by plugin id; only here are the two known
            // together, so this resolves the shared instance the container already built, not a fresh one.
            if (typeof(IPluginButtonHandler).IsAssignableFrom(implementationType))
                services.AddSingleton(sp => new PluginButtonBinding(
                    manifest.Id.ToString(),
                    (IPluginButtonHandler)sp.GetRequiredService(implementationType)));

            registered++;
        }

        // In interface order, not type-scan order, so the row's capability list reads the same
        // every run.
        foreach (var (extensionInterface, capability) in CapabilityInterfaces)
            if (extensionTypes.Any(t => extensionInterface.IsAssignableFrom(t)))
                plugin.Capabilities.Add(capability);

        settings.SetDefinitions(manifest.Id.ToString(), manifest.Settings);

        RegisterSettings(services, plugin, concreteTypes, assembly, configuration.GetSection(PluginSettingsConfiguration.SectionFor(manifest.Id.ToString())));

        // Optional: a plugin that only exposes providers needs no entry point, and loads as before.
        foreach (var type in concreteTypes.Where(t => typeof(IPlugin).IsAssignableFrom(t)))
        {
            var entryPointType = type;

            services.AddSingleton<LoadedPlugin>(serviceProvider => new LoadedPlugin(
                plugin,
                (IPlugin)ActivatorUtilities.CreateInstance(serviceProvider, entryPointType),
                CreateContext(serviceProvider)));

            registered++;
        }

        if (registered == 0)
            plugin.Warnings.Add("No extension implementations found in the entry assembly.");
    }

    /// <summary>Builds an extension, handing it this plugin's context only when a constructor asks for one.</summary>
    /// <remarks>ActivatorUtilities refuses an argument no constructor takes, and a plugin reading its
    /// settings through options may well need no context at all.</remarks>
    internal static object CreateExtension(IServiceProvider services, Type type, Func<IPluginContext> context)
        => type.GetConstructors().Any(c => c.GetParameters().Any(p => p.ParameterType == typeof(IPluginContext)))
            ? ActivatorUtilities.CreateInstance(services, type, context())
            : ActivatorUtilities.CreateInstance(services, type);

    /// <summary>Binds the settings class a plugin names with <see cref="IPlugin{TSettings}"/>, so its
    /// constructors can take <c>IOptions</c>, <c>IOptionsSnapshot</c> or <c>IOptionsMonitor</c> of it.</summary>
    /// <remarks>A plugin naming a host or shared type would bind its settings over that type for every
    /// plugin, so only a class from its own entry assembly is bound.</remarks>
    internal static void RegisterSettings(
        IServiceCollection services, DiscoveredPlugin plugin, IEnumerable<Type> types, Assembly entryAssembly, IConfiguration section)
    {
        var named = types
            .SelectMany(t => t.GetInterfaces())
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IPlugin<>))
            .Select(i => i.GetGenericArguments()[0])
            .Distinct()
            .ToList();

        if (named.Count == 0) return;

        if (named.Count > 1)
        {
            plugin.Warnings.Add($"Names more than one settings class ({string.Join(", ", named.Select(t => t.Name))}); none is bound.");
            return;
        }

        var settingsType = named[0];

        if (settingsType.Assembly != entryAssembly)
        {
            plugin.Warnings.Add($"Its settings class {settingsType.FullName} is not its own; it is not bound.");
            return;
        }

        services.AddOptions();
        ConfigureMethod.MakeGenericMethod(settingsType).Invoke(null, [services, section]);
    }

    private static readonly MethodInfo ConfigureMethod = typeof(OptionsConfigurationServiceCollectionExtensions)
        .GetMethod(nameof(OptionsConfigurationServiceCollectionExtensions.Configure), [typeof(IServiceCollection), typeof(IConfiguration)])!;

    // "1.0.0" must equal an assembly's 1.0.0.0: Version treats absent components as -1.
    private static Version Normalize(Version version)
        => new(version.Major, version.Minor, Math.Max(version.Build, 0), Math.Max(version.Revision, 0));

    private static DiscoveredPlugin Errored(string directory, PluginManifest? manifest, string error) => new()
    {
        Directory = directory,
        Manifest = manifest,
        Status = PluginStatus.Errored,
        Error = error,
    };

    private static DiscoveredPlugin Incompatible(string directory, PluginManifest manifest, string reason) => new()
    {
        Directory = directory,
        Manifest = manifest,
        Status = PluginStatus.Incompatible,
        Error = reason,
    };
}
