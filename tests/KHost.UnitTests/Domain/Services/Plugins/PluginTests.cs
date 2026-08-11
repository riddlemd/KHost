using KHost.Plugins.Sdk;
using KHost.Plugins.Sdk.Models;
using System.Text.Json;
using KHost.Domain.Services.Plugins;

namespace KHost.UnitTests.Domain.Services.Plugins;

public class PluginTests
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

    private static Plugin CreatePlugin(Dictionary<string, JsonElement>? stored = null, JsonElement? defaultValue = null)
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

        return new Plugin(manifest, stored);
    }

    private class TestSettings
    {
        public string Name { get; set; } = "initializer";
        public int PageSize { get; set; } = 10;
    }
}
