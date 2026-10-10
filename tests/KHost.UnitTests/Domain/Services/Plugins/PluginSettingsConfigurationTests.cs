using System.Text.Json;
using KHost.Abstractions.Models.Plugins;
using KHost.Domain.Services.Plugins;
using Microsoft.Extensions.Configuration;

namespace KHost.UnitTests.Domain.Services.Plugins;

public class PluginSettingsConfigurationTests
{
    private const string Id = "plugin-a";

    private static PluginSettingDefinition Setting(string key, PluginSettingType type, object? @default = null) => new()
    {
        Key = key, Type = type, Label = key, Default = @default is null ? null : JsonSerializer.SerializeToElement(@default),
    };

    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);

    private static (PluginSettingsConfiguration Source, IConfiguration Section) Build(
        Dictionary<string, JsonElement>? saved = null, params PluginSettingDefinition[] definitions)
    {
        var source = new PluginSettingsConfiguration(saved is null ? null : new Dictionary<string, Dictionary<string, JsonElement>> { [Id] = saved });
        source.SetDefinitions(Id, definitions);
        var root = new ConfigurationBuilder().Add(source).Build();
        return (source, root.GetSection(PluginSettingsConfiguration.SectionFor(Id)));
    }

    [Fact]
    public void AKeyWithNothingSaved_ReadsTheManifestDefault()
    {
        var (_, section) = Build(null, Setting("pageSize", PluginSettingType.Int, 10));

        Assert.Equal("10", section["pageSize"]);
    }

    [Fact]
    public void ASavedValue_StandsOverTheDefault_ByKeyInAnyCase()
    {
        var (_, section) = Build(new() { ["PAGESIZE"] = Json(25) }, Setting("pageSize", PluginSettingType.Int, 10));

        Assert.Equal("25", section["pageSize"]);
    }

    /// <summary>The binder reads text: a string is its contents, and a number or bool its literal.</summary>
    [Fact]
    public void EachSettingType_IsTheTextTheBinderReadsBack()
    {
        var (_, section) = Build(
            new() { ["name"] = Json("Toto"), ["shuffle"] = Json(true), ["pageSize"] = Json(25) },
            Setting("name", PluginSettingType.String), Setting("shuffle", PluginSettingType.Bool), Setting("pageSize", PluginSettingType.Int));

        Assert.Equal("Toto", section["name"]);
        Assert.Equal("true", section["shuffle"]);
        Assert.Equal("25", section["pageSize"]);
    }

    /// <summary>A hand-edited value of the wrong type would make every read of the plugin's options throw.</summary>
    [Theory]
    [InlineData("pageSize", "abc")]
    [InlineData("shuffle", "yes")]
    public void ASavedValueOfTheWrongType_IsDropped_AndTheDefaultStands(string key, string bad)
    {
        var (_, section) = Build(
            new() { [key] = Json(bad) },
            Setting("pageSize", PluginSettingType.Int, 10), Setting("shuffle", PluginSettingType.Bool, false));

        Assert.Equal(key == "pageSize" ? "10" : "false", section[key]);
    }

    [Fact]
    public void ASavedFractionForAnIntSetting_IsDropped()
    {
        var (_, section) = Build(new() { ["pageSize"] = Json(2.5) }, Setting("pageSize", PluginSettingType.Int, 10));

        Assert.Equal("10", section["pageSize"]);
    }

    [Fact]
    public void AnotherPluginsSettings_StayInTheirOwnSection()
    {
        var (source, section) = Build(null, Setting("pageSize", PluginSettingType.Int, 10));

        source.SetSaved("plugin-b", new Dictionary<string, JsonElement> { ["pageSize"] = Json(99) });

        Assert.Equal("10", section["pageSize"]);
    }

    /// <summary>The reload is what moves a plugin's IOptionsMonitor.</summary>
    [Fact]
    public void SetSaved_ReloadsWithTheNewValue()
    {
        var (source, section) = Build(null, Setting("pageSize", PluginSettingType.Int, 10));
        var reloaded = false;
        section.GetReloadToken().RegisterChangeCallback(_ => reloaded = true, null);

        source.SetSaved(Id, new Dictionary<string, JsonElement> { ["pageSize"] = Json(25) });

        Assert.True(reloaded);
        Assert.Equal("25", section["pageSize"]);
    }

    /// <summary>Clearing a field on the Plugins page saves it without the key, so the default comes back.</summary>
    [Fact]
    public void SetSaved_WithoutAKey_BringsBackItsDefault()
    {
        var (source, section) = Build(new() { ["pageSize"] = Json(25) }, Setting("pageSize", PluginSettingType.Int, 10));

        source.SetSaved(Id, new Dictionary<string, JsonElement>());

        Assert.Equal("10", section["pageSize"]);
    }
}
