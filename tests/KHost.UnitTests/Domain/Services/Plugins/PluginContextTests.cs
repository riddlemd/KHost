using KHost.Abstractions.Models.Plugins;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using System.Text.Json;
using KHost.Domain.Services.Plugins;
using KHost.Domain.Services.Plugins.Secrets;
using KHost.Secrets;
using KHost.UnitTests.Secrets;
using KHost.Domain.Services.Screens;

namespace KHost.UnitTests.Domain.Services.Plugins;

public class PluginContextTests
{
    [Fact]
    public void GetSetting_StoredValue_ReturnsIt()
    {
        var plugin = CreatePlugin(stored: new() { ["apiKey"] = JsonSerializer.SerializeToElement("abc123") });

        Assert.Equal("abc123", plugin.GetSetting<string>("apiKey"));
    }

    [Fact]
    public void GetSetting_KeyDiffersOnlyByCase_ReturnsStoredValue()
    {
        var plugin = CreatePlugin(stored: new() { ["apiKey"] = JsonSerializer.SerializeToElement("abc123") });

        Assert.Equal("abc123", plugin.GetSetting<string>("ApiKey"));
    }

    [Fact]
    public void GetSetting_MissingValueWithManifestDefault_ReturnsDefault()
    {
        var plugin = CreatePlugin(defaultValue: JsonSerializer.SerializeToElement(25));

        Assert.Equal(25, plugin.GetSetting<int>("PageSize"));
    }

    [Fact]
    public void GetSetting_StoredValueOverridesManifestDefault_ReturnsStored()
    {
        var plugin = CreatePlugin(
            stored: new() { ["pageSize"] = JsonSerializer.SerializeToElement(50) },
            defaultValue: JsonSerializer.SerializeToElement(25));

        Assert.Equal(50, plugin.GetSetting<int>("PageSize"));
    }

    [Fact]
    public void GetSetting_MissingValueNoDefault_ReturnsTypeDefault()
    {
        var plugin = CreatePlugin();

        Assert.Null(plugin.GetSetting<string>("Unset"));
        Assert.Equal(0, plugin.GetSetting<int>("Unset"));
    }

    [Fact]
    public void GetSetting_StoredValueOfWrongType_ReturnsTypeDefault()
    {
        var plugin = CreatePlugin(stored: new() { ["pageSize"] = JsonSerializer.SerializeToElement("not a number") });

        Assert.Equal(0, plugin.GetSetting<int>("PageSize"));
    }

    [Fact]
    public void BindSettings_StoredValues_MapToProperties()
    {
        var plugin = CreatePlugin(stored: new()
        {
            ["name"] = JsonSerializer.SerializeToElement("stored-name"),
            ["pageSize"] = JsonSerializer.SerializeToElement(50),
        });

        var settings = plugin.BindSettings<TestSettings>();

        Assert.Equal("stored-name", settings.Name);
        Assert.Equal(50, settings.PageSize);
    }

    [Fact]
    public void BindSettings_MissingKey_ManifestDefaultWins()
    {
        var plugin = CreatePlugin(defaultValue: JsonSerializer.SerializeToElement(25));

        Assert.Equal(25, plugin.BindSettings<TestSettings>().PageSize);
    }

    [Fact]
    public void BindSettings_NoStoredOrManifestValue_KeepsPropertyInitializer()
    {
        var plugin = CreatePlugin();

        var settings = plugin.BindSettings<TestSettings>();

        Assert.Equal("initializer", settings.Name);
        Assert.Equal(10, settings.PageSize);
    }

    [Fact]
    public void BindSettings_MalformedStoredValue_FallsBackToTypeDefaults()
    {
        var plugin = CreatePlugin(stored: new() { ["pageSize"] = JsonSerializer.SerializeToElement("not a number") });

        Assert.Equal(10, plugin.BindSettings<TestSettings>().PageSize);
    }

    private static PluginContext CreatePlugin(Dictionary<string, JsonElement>? stored = null, JsonElement? defaultValue = null)
    {
        var manifest = new PluginManifest
        {
            Id = Guid.Parse("00700000-0000-4000-8000-000000000701"),
            Name = "Test",
            Version = "1.0.0",
            EntryAssembly = "Test.dll",
            ApiVersion = PluginApi.CurrentVersion,
            Settings =
            [
                new PluginSettingDefinition
                {
                    Key = "PageSize",
                    Type = PluginSettingType.Int,
                    Label = "Page Size",
                    Default = defaultValue,
                },
            ],
        };

        return new PluginContext(manifest, stored, new DiscoveredPlugin { Directory = "/plugins/test", Manifest = manifest },
            new PluginSecretStore(new InMemorySecretStore()), Substitute.For<IScreenQrCodeService>());
    }

    /// <summary>A secret's name comes from the manifest, so two plugins can both use "session".</summary>
    [Fact]
    public async Task Secrets_AreFiledUnderThePluginsOwnId()
    {
        var store = new PluginSecretStore(new InMemorySecretStore());
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        var one = ContextFor(first, store);
        var two = ContextFor(second, store);

        await one.SetSecretAsync("session", "one");
        await two.SetSecretAsync("session", "two");

        // Read back through the contexts, not the store: reading under the wrong name is as much a
        // leak as writing under one, and a test that queries the store directly sees neither.
        Assert.Equal("one", await one.GetSecretAsync("session"));
        Assert.Equal("two", await two.GetSecretAsync("session"));
    }

    [Fact]
    public async Task Secrets_ReadBackWhatWasWritten()
    {
        var context = ContextFor(Guid.NewGuid(), new PluginSecretStore(new InMemorySecretStore()));

        Assert.Null(await context.GetSecretAsync("session"));

        await context.SetSecretAsync("session", "sk-1");

        Assert.Equal("sk-1", await context.GetSecretAsync("session"));
    }

    [Fact]
    public async Task Secrets_ClearedByAnEmptyValue()
    {
        var context = ContextFor(Guid.NewGuid(), new PluginSecretStore(new InMemorySecretStore()));
        await context.SetSecretAsync("session", "sk-1");

        await context.SetSecretAsync("session", null);

        Assert.Null(await context.GetSecretAsync("session"));
    }

    private static PluginContext ContextFor(Guid id, IPluginSecretStore store, IScreenQrCodeService? qrCodes = null)
    {
        var manifest = new PluginManifest
        {
            Id = id,
            Name = "Test",
            Version = "1.0.0",
            EntryAssembly = "Test.dll",
            ApiVersion = PluginApi.CurrentVersion,
        };

        return new PluginContext(manifest, null,
            new DiscoveredPlugin { Directory = "/plugins/test", Manifest = manifest }, store,
            qrCodes ?? Substitute.For<IScreenQrCodeService>());
    }

    private class TestSettings
    {
        public string Name { get; set; } = "initializer";
        public int PageSize { get; set; } = 10;
    }
}
