using KHost.Abstractions.Models.Plugins;
using KHost.Domain.Services.MediaProviders;
using KHost.Domain.Services.Plugins;
using KHost.Plugins.Sdk;
using KHost.Plugins.Sdk.Models;
using KHost.Plugins.Sdk.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace KHost.UnitTests.Domain.Services.Plugins;

public class PluginLoaderTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("khost-plugins-test-");

    private string PluginsDir => Path.Combine(_root.FullName, "plugins");

    // Windows keeps a loaded assembly mapped for the life of the process, so the entry dlls the
    // LoadAndRegister tests copied in cannot be deleted here; POSIX unlinks them regardless. Take
    // what the OS will give rather than failing every test in the class on the way out.
    public void Dispose()
    {
        try { _root.Delete(recursive: true); }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
    }

    [Fact]
    public void Discover_PluginsDirectoryMissing_ReturnsEmpty()
    {
        var plugins = PluginLoader.Discover(PluginsDir, new PluginsState());

        Assert.Empty(plugins);
    }

    [Fact]
    public void Discover_FolderWithoutManifest_ReportsErrored()
    {
        Directory.CreateDirectory(Path.Combine(PluginsDir, "no-manifest"));

        var plugin = Assert.Single(PluginLoader.Discover(PluginsDir, new PluginsState()));

        Assert.Equal(PluginStatus.Errored, plugin.Status);
        Assert.Contains("manifest.json", plugin.Error);
    }

    [Fact]
    public void Discover_InvalidManifestJson_ReportsErrored()
    {
        WriteRawManifest("broken", "{ not json");

        var plugin = Assert.Single(PluginLoader.Discover(PluginsDir, new PluginsState()));

        Assert.Equal(PluginStatus.Errored, plugin.Status);
    }

    [Fact]
    public void Discover_ManifestMissingRequiredFields_ReportsErrored()
    {
        WriteRawManifest("partial", """{ "id": "0a000000-0000-4000-8000-000000000a01", "name": "Partial" }""");

        var plugin = Assert.Single(PluginLoader.Discover(PluginsDir, new PluginsState()));

        Assert.Equal(PluginStatus.Errored, plugin.Status);
    }

    [Fact]
    public void Discover_DuplicateId_SecondReportsErrored()
    {
        WritePlugin("a-first", "0d000000-0000-4000-8000-00000000d0be");
        WritePlugin("b-second", "0d000000-0000-4000-8000-00000000d0be");

        var plugins = PluginLoader.Discover(PluginsDir, new PluginsState());

        Assert.Equal(PluginStatus.Disabled, plugins[0].Status);
        Assert.Equal(PluginStatus.Errored, plugins[1].Status);
        Assert.Contains("Duplicate", plugins[1].Error);
    }

    [Fact]
    public void Discover_ApiVersionMismatch_ReportsIncompatible()
    {
        WritePlugin("future", "0f000000-0000-4000-8000-000000f07072", apiVersion: PluginApi.CurrentVersion + 1);

        var plugin = Assert.Single(PluginLoader.Discover(PluginsDir, new PluginsState()));

        Assert.Equal(PluginStatus.Incompatible, plugin.Status);
    }

    [Fact]
    public void Discover_EntryAssemblyMissing_ReportsErrored()
    {
        WritePlugin("no-dll", "00d00000-0000-4000-8000-000000000d11", createEntryAssembly: false);

        var plugin = Assert.Single(PluginLoader.Discover(PluginsDir, new PluginsState()));

        Assert.Equal(PluginStatus.Errored, plugin.Status);
        Assert.Contains("Entry assembly", plugin.Error);
    }

    [Fact]
    public void Discover_EnabledInState_ReportsEnabled()
    {
        WritePlugin("mine", "00e00000-0000-4000-8000-00000000e000");
        var state = new PluginsState { EnabledPluginIds = ["00e00000-0000-4000-8000-00000000e000"] };

        var plugin = Assert.Single(PluginLoader.Discover(PluginsDir, state));

        Assert.Equal(PluginStatus.Enabled, plugin.Status);
    }

    [Fact]
    public void Discover_NotInEnabledList_ReportsDisabled()
    {
        WritePlugin("mine", "00e00000-0000-4000-8000-00000000e000");

        var plugin = Assert.Single(PluginLoader.Discover(PluginsDir, new PluginsState()));

        Assert.Equal(PluginStatus.Disabled, plugin.Status);
    }

    [Fact]
    public void LoadAndRegister_EntryAssemblyNotAnAssembly_ReportsErroredAndDoesNotThrow()
    {
        WritePlugin("garbage", "06a00000-0000-4000-8000-0000006a0ba6");
        var state = new PluginsState { EnabledPluginIds = ["06a00000-0000-4000-8000-0000006a0ba6"] };
        var plugins = PluginLoader.Discover(PluginsDir, state);
        var services = new ServiceCollection();

        PluginLoader.LoadAndRegister(services, plugins, state);

        Assert.Equal(PluginStatus.Errored, plugins[0].Status);
        Assert.Empty(services);
    }

    [Fact]
    public void LoadAndRegister_RealAssemblyWithoutExtensions_LoadsWithWarning()
    {
        // The Sdk dll is a convenient real assembly that contains no extension implementations.
        var directory = WritePlugin("sdk-copy", "05d00000-0000-4000-8000-0000005dc09e", entryAssembly: "Entry.dll", createEntryAssembly: false);
        File.Copy(typeof(PluginManifest).Assembly.Location, Path.Combine(directory, "Entry.dll"));
        var state = new PluginsState { EnabledPluginIds = ["05d00000-0000-4000-8000-0000005dc09e"] };
        var plugins = PluginLoader.Discover(PluginsDir, state);

        PluginLoader.LoadAndRegister(new ServiceCollection(), plugins, state);

        Assert.Equal(PluginStatus.Loaded, plugins[0].Status);
        Assert.Contains(plugins[0].Warnings, w => w.Contains("No extension implementations"));
        // Manifest "1.0.0" vs assembly 1.0.0.0 is the same version, not drift.
        Assert.DoesNotContain(plugins[0].Warnings, w => w.Contains("differs from assembly version"));
    }

    [Fact]
    public void LoadAndRegister_AssemblyWithExtensions_RecordsOneCapabilityPerInterface()
    {
        // KHost.Domain is a real assembly holding every extension shape, and nine rotation modes —
        // enough to prove the label is per interface, not per implementation.
        var directory = WritePlugin("domain-copy", "0ca00000-0000-4000-8000-0000000cab11", entryAssembly: "Entry.dll", createEntryAssembly: false);
        File.Copy(typeof(LocalMediaProvider).Assembly.Location, Path.Combine(directory, "Entry.dll"));
        var state = new PluginsState { EnabledPluginIds = ["0ca00000-0000-4000-8000-0000000cab11"] };
        var plugins = PluginLoader.Discover(PluginsDir, state);

        PluginLoader.LoadAndRegister(new ServiceCollection(), plugins, state);

        Assert.Equal(PluginStatus.Loaded, plugins[0].Status);
        Assert.Equal(["Media provider", "Queue rotation", "Break music"], plugins[0].Capabilities);
    }

    // A plugin's break music provider has to land in the container under the interface the service
    // resolves, or it loads, shows its capability on the Plugins page, and is never offered.
    [Fact]
    public void LoadAndRegister_AssemblyWithABreakMusicProvider_RegistersItForResolution()
    {
        var directory = WritePlugin("break-music", "0cd00000-0000-4000-8000-0000000cadd0", entryAssembly: "Entry.dll", createEntryAssembly: false);
        File.Copy(typeof(LocalMediaProvider).Assembly.Location, Path.Combine(directory, "Entry.dll"));
        var state = new PluginsState { EnabledPluginIds = ["0cd00000-0000-4000-8000-0000000cadd0"] };
        var plugins = PluginLoader.Discover(PluginsDir, state);
        var services = new ServiceCollection();

        PluginLoader.LoadAndRegister(services, plugins, state);

        Assert.Contains(services, d => d.ServiceType == typeof(IBreakMusicProvider));
    }

    [Fact]
    public void LoadAndRegister_AssemblyWithoutExtensions_RecordsNoCapabilities()
    {
        var directory = WritePlugin("sdk-only", "0cb00000-0000-4000-8000-0000000cab00", entryAssembly: "Entry.dll", createEntryAssembly: false);
        File.Copy(typeof(PluginManifest).Assembly.Location, Path.Combine(directory, "Entry.dll"));
        var state = new PluginsState { EnabledPluginIds = ["0cb00000-0000-4000-8000-0000000cab00"] };
        var plugins = PluginLoader.Discover(PluginsDir, state);

        PluginLoader.LoadAndRegister(new ServiceCollection(), plugins, state);

        Assert.Empty(plugins[0].Capabilities);
    }

    [Fact]
    public void LoadAndRegister_DisabledPlugin_RecordsNoCapabilities()
    {
        var directory = WritePlugin("not-enabled", "0cc00000-0000-4000-8000-0000000cacc0", entryAssembly: "Entry.dll", createEntryAssembly: false);
        File.Copy(typeof(LocalMediaProvider).Assembly.Location, Path.Combine(directory, "Entry.dll"));
        var state = new PluginsState();
        var plugins = PluginLoader.Discover(PluginsDir, state);

        PluginLoader.LoadAndRegister(new ServiceCollection(), plugins, state);

        Assert.Equal(PluginStatus.Disabled, plugins[0].Status);
        Assert.Empty(plugins[0].Capabilities);
    }

    [Fact]
    public void ReadState_FileMissing_ReturnsDefaults()
    {
        var state = PluginLoader.ReadState(Path.Combine(_root.FullName, "cache"));

        Assert.Empty(state.EnabledPluginIds);
    }

    [Fact]
    public void ReadState_CorruptFile_ReturnsDefaults()
    {
        var cacheDir = Directory.CreateDirectory(Path.Combine(_root.FullName, "cache")).FullName;
        File.WriteAllText(Path.Combine(cacheDir, "plugins.json"), "{ nope");

        var state = PluginLoader.ReadState(cacheDir);

        Assert.Empty(state.EnabledPluginIds);
    }

    [Fact]
    public void ReadState_ValidFile_ReadsEnabledIds()
    {
        var cacheDir = Directory.CreateDirectory(Path.Combine(_root.FullName, "cache")).FullName;
        File.WriteAllText(Path.Combine(cacheDir, "plugins.json"), """{ "enabledPluginIds": ["khost.youtube"] }""");

        var state = PluginLoader.ReadState(cacheDir);

        Assert.Equal(["khost.youtube"], state.EnabledPluginIds);
    }

    private string WritePlugin(string folder, string id, int apiVersion = PluginApi.CurrentVersion,
        string entryAssembly = "Plugin.dll", bool createEntryAssembly = true)
    {
        var manifest = new
        {
            id,
            name = id,
            version = "1.0.0",
            entryAssembly,
            apiVersion,
        };
        var directory = WriteRawManifest(folder, JsonSerializer.Serialize(manifest));

        if (createEntryAssembly)
            File.WriteAllText(Path.Combine(directory, entryAssembly), "not a real assembly");

        return directory;
    }

    private string WriteRawManifest(string folder, string json)
    {
        var directory = Directory.CreateDirectory(Path.Combine(PluginsDir, folder)).FullName;

        File.WriteAllText(Path.Combine(directory, PluginLoader.ManifestFileName), json);

        return directory;
    }
}
