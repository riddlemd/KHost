using System.Text.Json;
using Bunit;
using KHost.Abstractions.Models.Plugins;
using KHost.Abstractions.Services;
using KHost.Plugins.Sdk.Models;
using KHost.UserInterface.Components.Pages.Settings;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;

namespace KHost.UnitTests.UserInterface.Components.Pages.Settings;

public class PluginsManagerPageTests : BunitContext
{
    private const string RowSelector = ".kh-plugins-manager__row";
    private const string DisclosureSelector = ".kh-plugins-manager__disclosure";
    private const string EnableSwitchSelector = ".kh-plugins-manager__aside .kh-switch";
    private const string StateBadgeSelector = ".kh-plugins-manager__aside .kh-badge";
    private const string SettingInputSelector = ".kh-plugins-manager__field .kh-form-control";
    private const string SaveButtonSelector = "button[type=submit]";
    private const string SecretStateSelector = ".kh-plugins-manager__secret-state";
    private const string ChipSelector = ".kh-plugins-manager__chip";

    private static readonly Guid PluginId = new("11111111-1111-4111-8111-111111111111");

    private readonly IPluginsService _pluginsService = Substitute.For<IPluginsService>();
    private readonly IExternalLinkService _externalLinks = Substitute.For<IExternalLinkService>();

    public PluginsManagerPageTests()
    {
        _pluginsService.ReadEnabledIdsAsync().Returns(new HashSet<string>());
        _pluginsService.ReadSettingsAsync(Arg.Any<string>()).Returns(new Dictionary<string, JsonElement>());

        Services.AddSingleton(_pluginsService);
        Services.AddSingleton(_externalLinks);
    }

    [Fact]
    public void Row_IsCollapsedUntilTheDisclosureIsClicked()
    {
        Arrange(Plugin(PluginStatus.Loaded, Setting("maxResults", PluginSettingType.Int, "Max Results")), enabled: true);

        var cut = Render<PluginsManagerPage>();

        Assert.Empty(cut.FindAll(SettingInputSelector));

        cut.Find(DisclosureSelector).Click();

        Assert.Single(cut.FindAll(SettingInputSelector));
    }

    [Fact]
    public void Row_ShowsTheCapabilitiesTheLoaderRecorded()
    {
        var plugin = Plugin(PluginStatus.Loaded);
        plugin.Capabilities.Add("Media provider");
        Arrange(plugin, enabled: true);

        var cut = Render<PluginsManagerPage>();

        Assert.Equal("Media provider", cut.Find(ChipSelector).TextContent.Trim());
    }

    [Fact]
    public void StateBadge_LoadedAndStillEnabled_ReadsRunning()
    {
        Arrange(Plugin(PluginStatus.Loaded), enabled: true);

        var cut = Render<PluginsManagerPage>();

        Assert.Equal("Running", cut.Find(StateBadgeSelector).TextContent.Trim());
    }

    [Fact]
    public void StateBadge_EnabledSinceStartup_ReadsRestartToLoad()
    {
        // Discovered as Disabled at startup, enabled afterwards — the pending state the old page
        // reported as plain "Disabled".
        Arrange(Plugin(PluginStatus.Disabled), enabled: true);

        var cut = Render<PluginsManagerPage>();

        Assert.Equal("Restart to load", cut.Find(StateBadgeSelector).TextContent.Trim());
    }

    [Fact]
    public void StateBadge_DisabledWhileStillLoaded_ReadsRestartToUnload()
    {
        Arrange(Plugin(PluginStatus.Loaded), enabled: false);

        var cut = Render<PluginsManagerPage>();

        Assert.Equal("Restart to unload", cut.Find(StateBadgeSelector).TextContent.Trim());
    }

    [Fact]
    public void StateBadge_NeverEnabled_ReadsOff()
    {
        Arrange(Plugin(PluginStatus.Disabled), enabled: false);

        var cut = Render<PluginsManagerPage>();

        Assert.Equal("Off", cut.Find(StateBadgeSelector).TextContent.Trim());
    }

    [Fact]
    public void StateBadge_ErroredPlugin_ReadsFailed()
    {
        var plugin = Plugin(PluginStatus.Errored);
        plugin.Error = "Entry assembly 'Missing.dll' not found.";
        Arrange(plugin, enabled: false);

        var cut = Render<PluginsManagerPage>();

        Assert.Equal("Failed", cut.Find(StateBadgeSelector).TextContent.Trim());
    }

    [Fact]
    public void EnableSwitch_Clicked_EnablesThroughTheService()
    {
        Arrange(Plugin(PluginStatus.Disabled), enabled: false);

        var cut = Render<PluginsManagerPage>();
        cut.Find(EnableSwitchSelector).Click();

        _pluginsService.Received(1).SetEnabledAsync(PluginId.ToString(), true);
        Assert.Equal("true", cut.Find(EnableSwitchSelector).GetAttribute("aria-checked"));
    }

    [Fact]
    public void EnableSwitch_ErroredPlugin_IsDisabled()
    {
        var plugin = Plugin(PluginStatus.Errored);
        plugin.Error = "Broken.";
        Arrange(plugin, enabled: false);

        var cut = Render<PluginsManagerPage>();

        Assert.True(cut.Find(EnableSwitchSelector).HasAttribute("disabled"));
    }

    [Fact]
    public void SaveButton_AppearsOnlyOnceASettingChanges()
    {
        Arrange(Plugin(PluginStatus.Loaded, Setting("maxResults", PluginSettingType.Int, "Max Results")), enabled: true);

        var cut = Render<PluginsManagerPage>();
        cut.Find(DisclosureSelector).Click();

        Assert.Empty(cut.FindAll(SaveButtonSelector));

        cut.Find(SettingInputSelector).Input("25");

        Assert.Single(cut.FindAll(SaveButtonSelector));
    }

    [Fact]
    public void Revert_RestoresTheStoredValueAndHidesTheSaveButton()
    {
        Arrange(Plugin(PluginStatus.Loaded, Setting("maxResults", PluginSettingType.Int, "Max Results")), enabled: true,
            stored: new() { ["maxResults"] = Json(10) });

        var cut = Render<PluginsManagerPage>();
        cut.Find(DisclosureSelector).Click();
        cut.Find(SettingInputSelector).Input("25");
        cut.Find(".kh-plugins-manager__foot .kh-button--secondary").Click();

        Assert.Empty(cut.FindAll(SaveButtonSelector));
        Assert.Equal("10", cut.Find(SettingInputSelector).GetAttribute("value"));
    }

    [Fact]
    public void Save_WritesTheEditedValue()
    {
        Arrange(Plugin(PluginStatus.Loaded, Setting("maxResults", PluginSettingType.Int, "Max Results")), enabled: true,
            stored: new() { ["maxResults"] = Json(10) });

        var cut = Render<PluginsManagerPage>();
        cut.Find(DisclosureSelector).Click();
        cut.Find(SettingInputSelector).Input("25");
        cut.Find(SaveButtonSelector).Click();

        Assert.Equal(25, CapturedSave()["maxResults"].GetInt32());
    }

    [Fact]
    public void Save_EditingAnotherField_KeepsTheStoredSecret()
    {
        // SaveSettingsAsync replaces a plugin's whole value set, so a secret the page never renders
        // is deleted by any unrelated save unless it is written back.
        Arrange(
            Plugin(PluginStatus.Loaded,
                Setting("maxResults", PluginSettingType.Int, "Max Results"),
                Setting("apiKey", PluginSettingType.String, "API Key", secret: true)),
            enabled: true,
            stored: new() { ["maxResults"] = Json(10), ["apiKey"] = Json("sk-live-super-secret") });

        var cut = Render<PluginsManagerPage>();
        cut.Find(DisclosureSelector).Click();
        cut.Find(SettingInputSelector).Input("25");
        cut.Find(SaveButtonSelector).Click();

        Assert.Equal("sk-live-super-secret", CapturedSave()["apiKey"].GetString());
    }

    [Fact]
    public void Secret_WithAStoredValue_ReportsItIsSetWithoutRenderingIt()
    {
        Arrange(
            Plugin(PluginStatus.Loaded, Setting("apiKey", PluginSettingType.String, "API Key", secret: true)),
            enabled: true,
            stored: new() { ["apiKey"] = Json("sk-live-super-secret") });

        var cut = Render<PluginsManagerPage>();
        cut.Find(DisclosureSelector).Click();

        Assert.Contains("Set — ends cret", cut.Find(SecretStateSelector).TextContent);
        Assert.DoesNotContain("sk-live-super-secret", cut.Markup);
    }

    [Fact]
    public void Secret_WithoutAStoredValue_OffersAnEmptyInput()
    {
        Arrange(
            Plugin(PluginStatus.Loaded, Setting("apiKey", PluginSettingType.String, "API Key", secret: true)),
            enabled: true);

        var cut = Render<PluginsManagerPage>();
        cut.Find(DisclosureSelector).Click();

        Assert.Empty(cut.FindAll(SecretStateSelector));
        Assert.Equal("password", cut.Find(SettingInputSelector).GetAttribute("type"));
    }

    [Fact]
    public void Secret_Cleared_IsDroppedOnSave()
    {
        Arrange(
            Plugin(PluginStatus.Loaded, Setting("apiKey", PluginSettingType.String, "API Key", secret: true)),
            enabled: true,
            stored: new() { ["apiKey"] = Json("sk-live-super-secret") });

        var cut = Render<PluginsManagerPage>();
        cut.Find(DisclosureSelector).Click();
        cut.Find(".kh-button--outline-danger").Click();
        cut.Find(SaveButtonSelector).Click();

        Assert.DoesNotContain("apiKey", CapturedSave().Keys);
    }

    [Fact]
    public void Secret_Replaced_WritesTheNewValue()
    {
        Arrange(
            Plugin(PluginStatus.Loaded, Setting("apiKey", PluginSettingType.String, "API Key", secret: true)),
            enabled: true,
            stored: new() { ["apiKey"] = Json("sk-live-super-secret") });

        var cut = Render<PluginsManagerPage>();
        cut.Find(DisclosureSelector).Click();
        cut.Find(".kh-plugins-manager__secret .kh-button--secondary").Click();
        cut.Find(SettingInputSelector).Input("sk-live-brand-new");
        cut.Find(SaveButtonSelector).Click();

        Assert.Equal("sk-live-brand-new", CapturedSave()["apiKey"].GetString());
    }

    [Fact]
    public void RestartBanner_NamesThePluginWaitingOnIt()
    {
        _pluginsService.RestartRequired.Returns(true);
        Arrange(Plugin(PluginStatus.Disabled), enabled: true);

        var cut = Render<PluginsManagerPage>();

        Assert.Contains("Test Plugin — restart KHost to apply.", cut.Find(".kh-plugins-manager__restart").TextContent);
    }

    [Fact]
    public void NoPlugins_RendersTheEmptyState()
    {
        _pluginsService.Plugins.Returns([]);

        var cut = Render<PluginsManagerPage>();

        Assert.Empty(cut.FindAll(RowSelector));
        Assert.Contains("No plugins installed", cut.Markup);
    }

    private Dictionary<string, JsonElement> CapturedSave()
        => (Dictionary<string, JsonElement>)_pluginsService.ReceivedCalls()
            .Single(c => c.GetMethodInfo().Name == nameof(IPluginsService.SaveSettingsAsync))
            .GetArguments()[1]!;

    private void Arrange(DiscoveredPlugin plugin, bool enabled, Dictionary<string, JsonElement>? stored = null)
    {
        _pluginsService.Plugins.Returns([plugin]);
        _pluginsService.ReadEnabledIdsAsync().Returns(enabled ? new HashSet<string> { plugin.Id } : []);
        _pluginsService.ReadSettingsAsync(plugin.Id).Returns(stored ?? []);
    }

    private static DiscoveredPlugin Plugin(PluginStatus status, params PluginSettingDefinition[] settings) => new()
    {
        Directory = Path.Combine("plugins", "test-plugin"),
        Status = status,
        Manifest = new PluginManifest
        {
            Id = PluginId,
            Name = "Test Plugin",
            Version = "1.0.0",
            Description = "A plugin used by the tests.",
            EntryAssembly = "Test.dll",
            ApiVersion = 1,
            Settings = [.. settings],
        },
    };

    private static PluginSettingDefinition Setting(string key, PluginSettingType type, string label, bool secret = false)
        => new() { Key = key, Type = type, Label = label, Secret = secret };

    private static JsonElement Json<T>(T value) => JsonSerializer.SerializeToElement(value);
}
