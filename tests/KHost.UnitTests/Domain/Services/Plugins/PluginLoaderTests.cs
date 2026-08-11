using KHost.Abstractions.Models.Plugins;
using KHost.Domain.Services.Plugins;
using KHost.Plugins.Sdk;
using KHost.Plugins.Sdk.Models;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace KHost.UnitTests.Domain.Services.Plugins;

public class PluginLoaderTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("khost-plugins-test-");

    private string PluginsDir => Path.Combine(_root.FullName, "plugins");

    public void Dispose() => _root.Delete(recursive: true);

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
