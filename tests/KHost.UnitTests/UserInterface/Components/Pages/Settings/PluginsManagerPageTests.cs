using AngleSharp.Html.Dom;
using Bunit;
using KHost.Abstractions.Models.Plugins;
using KHost.Abstractions.Services;
using KHost.Domain.Services.Messaging;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Models;
using KHost.UserInterface.Components.Pages.Settings;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using KHost.Common.Plugins;

namespace KHost.UnitTests.UserInterface.Components.Pages.Settings;

public class PluginsManagerPageTests : BunitContext
{
    private const string RowSelector = ".kh-plugins-manager__row";
    private const string DisclosureSelector = ".kh-plugins-manager__disclosure";
    private const string EnableToggleSelector = ".kh-plugins-manager__aside .kh-form-check-input";
    private const string StateBadgeSelector = ".kh-plugins-manager__aside .kh-badge";
    private const string SettingInputSelector = ".kh-plugins-manager__field .kh-form-control";
    private const string SectionSelector = ".kh-plugins-manager__section";
    private const string SaveButtonSelector = "button[type=submit]";
    private const string SecretStateSelector = ".kh-plugins-manager__secret-state";
    private const string ChipSelector = ".kh-plugins-manager__chip";
    private const string AvailableTabSelector = ".kh-plugins-manager__tab:last-child";
    private const string AvailableRowSelector = ".kh-plugins-manager__head--static";
    // The aside, not the row: the version chip beside the name is a .kh-badge too.
    private const string AvailableBadgeSelector = ".kh-plugins-manager__head--static .kh-plugins-manager__aside .kh-badge";

    private static readonly Guid PluginId = new("11111111-1111-4111-8111-111111111111");

    private readonly IPluginsService _pluginsService = Substitute.For<IPluginsService>();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);
    private readonly IExternalLinkService _externalLinks = Substitute.For<IExternalLinkService>();
    private readonly IPluginCatalogService _catalog = Substitute.For<IPluginCatalogService>();
    private readonly IPluginInstallerService _installer = Substitute.For<IPluginInstallerService>();
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();
    private readonly IPluginButtonService _buttons = Substitute.For<IPluginButtonService>();

    public PluginsManagerPageTests()
    {
        _pluginsService.ReadEnabledIdsAsync().Returns(new HashSet<string>());
        _pluginsService.ReadSettingsAsync(Arg.Any<string>()).Returns(new Dictionary<string, JsonElement>());
        _buttons.ButtonsFor(Arg.Any<string>()).Returns([]);

        Services.AddSingleton(_pluginsService);
        Services.AddSingleton<IMessageBroker>(_broker);
        Services.AddSingleton(_externalLinks);
        Services.AddSingleton(_catalog);
        Services.AddSingleton(_installer);
        Services.AddSingleton(_dialogs);
        Services.AddSingleton(_buttons);

        _installer.Snapshot().Returns([]);
        _installer.Staged().Returns(PluginStagingState.Empty);
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
        // Discovered as Disabled at startup, enabled afterwards: the pending state the old page
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
        cut.Find(EnableToggleSelector).Change(true);

        _pluginsService.Received(1).SetEnabledAsync(PluginId.ToString(), true);
        Assert.True(((IHtmlInputElement)cut.Find(EnableToggleSelector)).IsChecked);
    }

    [Fact]
    public void EnableSwitch_ErroredPlugin_IsDisabled()
    {
        var plugin = Plugin(PluginStatus.Errored);
        plugin.Error = "Broken.";
        Arrange(plugin, enabled: false);

        var cut = Render<PluginsManagerPage>();

        Assert.True(cut.Find(EnableToggleSelector).HasAttribute("disabled"));
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

        Assert.Contains("Set: ends cret", cut.Find(SecretStateSelector).TextContent);
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

        Assert.Contains("Test Plugin: restart KHost to apply.", cut.Find(".kh-plugins-manager__restart").TextContent);
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

    [Fact]
    public void Undo_AStagedFirstInstall_DisablesTheIdInstallingEnabled()
    {
        Arrange(Plugin(PluginStatus.Loaded), enabled: true);

        var fresh = Guid.NewGuid();

        _installer.Staged().Returns(new PluginStagingState { Installs = new HashSet<Guid> { fresh } });

        var cut = RenderAvailable(CatalogEntry(fresh, "Fresh", CatalogRelease("1.0.0")));

        cut.Find($"{AvailableRowSelector} .kh-button--secondary").Click();

        _installer.Received(1).ClearStaged(fresh);
        _pluginsService.Received(1).SetEnabledAsync(fresh.ToString(), false);
    }

    [Fact]
    public void Undo_AStagedUpdate_LeavesTheInstalledCopyEnabled()
    {
        // The installed copy keeps running, and it was enabled before the update was staged, so
        // dropping the payload must not switch it off.
        Arrange(Plugin(PluginStatus.Loaded), enabled: true);

        _installer.Staged().Returns(new PluginStagingState { Installs = new HashSet<Guid> { PluginId } });

        var cut = RenderAvailable(CatalogEntry(PluginId, "Test Plugin", CatalogRelease("2.0.0")));

        cut.Find($"{AvailableRowSelector} .kh-button--secondary").Click();

        _installer.Received(1).ClearStaged(PluginId);
        _pluginsService.DidNotReceive().SetEnabledAsync(PluginId.ToString(), false);
    }

    [Fact]
    public void Undo_APendingRemovalOnALoadedPlugin_EnablesItAgain()
    {
        Arrange(Plugin(PluginStatus.Loaded), enabled: false);

        _installer.Staged().Returns(new PluginStagingState { Removals = new HashSet<string> { "test-plugin" } });

        var cut = RenderAvailable(CatalogEntry(PluginId, "Test Plugin", CatalogRelease("1.0.0")));

        cut.Find($"{AvailableRowSelector} .kh-button--secondary").Click();

        _pluginsService.Received(1).SetEnabledAsync(PluginId.ToString(), true);
    }

    [Fact]
    public void Undo_APendingRemovalOnAPluginThatWasNeverLoaded_LeavesItOff()
    {
        Arrange(Plugin(PluginStatus.Disabled), enabled: false);

        _installer.Staged().Returns(new PluginStagingState { Removals = new HashSet<string> { "test-plugin" } });

        var cut = RenderAvailable(CatalogEntry(PluginId, "Test Plugin", CatalogRelease("1.0.0")));

        cut.Find($"{AvailableRowSelector} .kh-button--secondary").Click();

        _pluginsService.DidNotReceive().SetEnabledAsync(PluginId.ToString(), true);
    }

    /// <summary>Renders the page and switches to the browse list, where a catalog entry shows.</summary>
    private IRenderedComponent<PluginsManagerPage> RenderAvailable(params PluginCatalogEntry[] entries)
    {
        _catalog.Current.Returns(new PluginCatalogSnapshot
        {
            Catalog = new PluginCatalog { SchemaVersion = 1, Plugins = [.. entries] },
            FetchedUtc = new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc),
        });

        var cut = Render<PluginsManagerPage>();

        cut.Find(AvailableTabSelector).Click();

        return cut;
    }

    private static PluginCatalogEntry CatalogEntry(Guid id, string name, params PluginCatalogRelease[] releases)
        => new() { Id = id, Name = name, Releases = [.. releases] };

    private static PluginCatalogRelease CatalogRelease(
        string version,
        // The gate itself, not the number it currently holds: a release built for this host is
        // what these tests mean, and a literal here fails every time that legitimately moves.
        int apiVersion = PluginApi.CurrentVersion,
        string sha256 = "abc123",
        string url = "https://example.test/plugin.zip")
        => new() { Version = version, ApiVersion = apiVersion, Url = url, Sha256 = sha256 };

    [Fact]
    public void AvailableRow_ReleaseWithNoChecksum_ReadsNotVerifiableRatherThanNotCompatible()
    {
        Arrange(Plugin(PluginStatus.Loaded), enabled: true);

        // Both leave nothing installable, and the two are not the same problem: "not compatible"
        // would send a host looking for a KHost upgrade that changes nothing here.
        var cut = RenderAvailable(CatalogEntry(Guid.NewGuid(), "Unsigned", CatalogRelease("1.0.0", sha256: "")));

        Assert.Equal("Not verifiable", cut.Find(AvailableBadgeSelector).TextContent.Trim());
    }

    [Fact]
    public void AvailableRow_ReleaseServedOverPlainHttp_ReadsNotVerifiable()
    {
        Arrange(Plugin(PluginStatus.Loaded), enabled: true);

        var cut = RenderAvailable(CatalogEntry(Guid.NewGuid(), "Insecure",
            CatalogRelease("1.0.0", url: "http://example.test/plugin.zip")));

        Assert.Equal("Not verifiable", cut.Find(AvailableBadgeSelector).TextContent.Trim());
    }

    [Fact]
    public void AvailableRow_BuiltOnlyForOtherPlatforms_ReadsNotCompatibleAndSaysWhy()
    {
        // Same badge as a plugin-API mismatch (neither will run here), but the tooltip has to
        // name the platform, or the host is left to guess which kind of incompatible it is.
        Arrange(Plugin(PluginStatus.Loaded), enabled: true);

        var other = PluginRid.Current == "win" ? "linux" : "win";
        var release = CatalogRelease("1.0.0");
        release.Rid = other;

        var cut = RenderAvailable(CatalogEntry(Guid.NewGuid(), "Elsewhere", release));
        var badge = cut.Find(AvailableBadgeSelector);

        Assert.Equal("Not compatible", badge.TextContent.Trim());
        Assert.Contains(PluginRid.Current, badge.GetAttribute("title"));
    }

    [Fact]
    public void AvailableRow_EveryReleaseTargetsAnotherApi_SaysSoInTheTooltip()
    {
        Arrange(Plugin(PluginStatus.Loaded), enabled: true);

        var cut = RenderAvailable(CatalogEntry(Guid.NewGuid(), "Future", CatalogRelease("1.0.0", apiVersion: 99)));

        Assert.Contains("plugin API", cut.Find(AvailableBadgeSelector).GetAttribute("title"));
    }

    [Fact]
    public void AvailableRow_EveryReleaseTargetsAnotherApi_ReadsNotCompatible()
    {
        Arrange(Plugin(PluginStatus.Loaded), enabled: true);

        var cut = RenderAvailable(CatalogEntry(Guid.NewGuid(), "Future", CatalogRelease("1.0.0", apiVersion: 99)));

        Assert.Equal("Not compatible", cut.Find(AvailableBadgeSelector).TextContent.Trim());
    }

    [Fact]
    public void AvailableRow_NotInstalled_OffersInstall()
    {
        Arrange(Plugin(PluginStatus.Loaded), enabled: true);

        var cut = RenderAvailable(CatalogEntry(Guid.NewGuid(), "Available", CatalogRelease("2.0.0")));

        Assert.Contains("Install", cut.Find($"{AvailableRowSelector} button.kh-button").TextContent);
    }

    [Fact]
    public void AvailableRow_NewerThanTheInstalledVersion_OffersUpdate()
    {
        Arrange(Plugin(PluginStatus.Loaded), enabled: true);

        var cut = RenderAvailable(CatalogEntry(PluginId, "Test Plugin", CatalogRelease("2.0.0")));

        Assert.Contains("Update", cut.Find($"{AvailableRowSelector} button.kh-button").TextContent);
    }

    [Fact]
    public void AvailableRow_SameVersionAsTheInstalledOne_ReadsInstalled()
    {
        Arrange(Plugin(PluginStatus.Loaded), enabled: true);

        var cut = RenderAvailable(CatalogEntry(PluginId, "Test Plugin", CatalogRelease("1.0.0")));

        Assert.Equal("Installed", cut.Find(AvailableBadgeSelector).TextContent.Trim());
    }

    [Fact]
    public void Remove_TwoFoldersShareAnId_MarksOnlyTheFolderTheRowStandsFor()
    {
        ArrangeDuplicates();
        ConfirmDialogs();

        var cut = Render<PluginsManagerPage>();

        cut.FindAll(DisclosureSelector)[1].Click();
        cut.Find($"{RowSelector}--open .kh-button--outline-danger").Click();

        _installer.Received(1).MarkForRemoval("youtube-copy");
        _installer.DidNotReceive().MarkForRemoval("youtube");
    }

    [Fact]
    public void Remove_TwoFoldersShareAnId_LeavesTheOtherCopyEnabled()
    {
        ArrangeDuplicates();
        ConfirmDialogs();

        var cut = Render<PluginsManagerPage>();

        cut.FindAll(DisclosureSelector)[1].Click();
        cut.Find($"{RowSelector}--open .kh-button--outline-danger").Click();

        _pluginsService.DidNotReceive().SetEnabledAsync(PluginId.ToString(), false);
    }

    [Fact]
    public void PendingRemoval_TwoFoldersShareAnId_OnlyTheMarkedRowSaysSo()
    {
        ArrangeDuplicates();

        _installer.Staged().Returns(new PluginStagingState { Removals = new HashSet<string> { "youtube-copy" } });

        var badges = Render<PluginsManagerPage>().FindAll(StateBadgeSelector);

        Assert.DoesNotContain("Restart to remove", badges[0].TextContent);
        Assert.Contains("Restart to remove", badges[1].TextContent);
    }

    [Fact]
    public void KeepIt_TwoFoldersShareAnId_ClearsOnlyThatFoldersRemoval()
    {
        ArrangeDuplicates();

        _installer.Staged().Returns(new PluginStagingState { Removals = new HashSet<string> { "youtube-copy" } });

        var cut = Render<PluginsManagerPage>();

        cut.FindAll(DisclosureSelector)[1].Click();
        cut.Find($"{RowSelector}--open .kh-button--secondary").Click();

        _installer.Received(1).ClearRemoval("youtube-copy");
    }

    [Fact]
    public void Disclosure_TwoFoldersShareAnId_OpensOnlyTheRowItSitsOn()
    {
        ArrangeDuplicates();

        var cut = Render<PluginsManagerPage>();

        cut.FindAll(DisclosureSelector)[1].Click();

        Assert.Single(cut.FindAll($"{RowSelector}--open"));
    }

    /// <summary>A definition carries no sort group; reordering parts a setting from its kin.</summary>
    [Fact]
    public void Settings_AreShownInTheOrderTheManifestDeclares()
    {
        Arrange(Plugin(PluginStatus.Loaded,
            Setting("playlistUri", PluginSettingType.String, "Playlist"),
            Setting("shuffle", PluginSettingType.Bool, "Shuffle the playlist"),
            Setting("spicetifyBridge", PluginSettingType.Bool, "Listen for the extension"),
            Setting("spicetifyBridgePort", PluginSettingType.Int, "Port the extension connects on"),
            Setting("fadeMilliseconds", PluginSettingType.Int, "Fade length")), enabled: true);

        var cut = Render<PluginsManagerPage>();
        cut.Find(DisclosureSelector).Click();

        // Declared order exactly: not grouped by type, and not alphabetical either. Both of those
        // would move "Port the extension connects on" away from the toggle that turns it on.
        Assert.Equal(
            ["Playlist", "Shuffle the playlist", "Listen for the extension", "Port the extension connects on", "Fade length"],
            FieldLabels(cut));
    }

    // The two shapes label themselves differently, which is why a row each is what makes the panel
    // readable. Packed into columns, a checkbox sat beside an input.
    private static List<string> FieldLabels(IRenderedComponent<PluginsManagerPage> cut)
        => [.. cut.FindAll(".kh-plugins-manager__field .kh-plugins-manager__label, .kh-plugins-manager__field .kh-form-check-label")
            .Select(l => l.TextContent.Trim())];

    [Fact]
    public void Glyph_AManifestNamingOne_Wins()
    {
        var plugin = Plugin(PluginStatus.Loaded);
        plugin.Manifest!.Icon = "search";

        Arrange(plugin, enabled: true);

        Assert.NotEmpty(Render<PluginsManagerPage>().FindAll(".kh-plugins-manager__glyph .bi-search"));
    }

    /// <summary>The host once guessed a glyph from a capability, defying the catalog's own guess.</summary>
    [Fact]
    public void Glyph_ACapabilityWithoutAManifestIcon_IsStillTheGenericOne()
    {
        var plugin = Plugin(PluginStatus.Loaded);
        plugin.Capabilities.Add("Break music");

        Arrange(plugin, enabled: true);

        Assert.NotEmpty(Render<PluginsManagerPage>().FindAll(".kh-plugins-manager__glyph .bi-puzzle"));
    }

    /// <summary>An image specifier is not a glyph name; a bad image falls back like empty.</summary>
    [Fact]
    public void Glyph_TheImageSpecifierWithoutAUsableImage_FallsBackRatherThanNamingAGlyph()
    {
        var plugin = Plugin(PluginStatus.Loaded);
        plugin.Manifest!.Icon = PluginIcon.ImageSpecifier;

        Arrange(plugin, enabled: true);

        var cut = Render<PluginsManagerPage>();

        Assert.Empty(cut.FindAll(".kh-plugins-manager__glyph img"));
        Assert.NotEmpty(cut.FindAll(".kh-plugins-manager__glyph .bi-puzzle"));
    }

    [Fact]
    public void Glyph_AUsableImage_IsDrawnInsteadOfAGlyph()
    {
        var plugin = Plugin(PluginStatus.Loaded);
        plugin.Manifest!.Icon = PluginIcon.ImageSpecifier;
        plugin.HasIconImage = true;

        Arrange(plugin, enabled: true);

        var image = Render<PluginsManagerPage>().Find(".kh-plugins-manager__glyph img");

        Assert.Equal($"/plugins/{PluginId}/icon.png", image.GetAttribute("src"));
    }

    [Fact]
    public void Glyph_NothingSpecified_IsTheGenericOne()
    {
        Arrange(Plugin(PluginStatus.Loaded), enabled: true);

        Assert.NotEmpty(Render<PluginsManagerPage>().FindAll(".kh-plugins-manager__glyph .bi-puzzle"));
    }

    private void ArrangeDuplicates()
    {
        var loaded = Plugin(PluginStatus.Loaded, folderName: "youtube");
        var duplicate = Plugin(PluginStatus.Errored, folderName: "youtube-copy");

        duplicate.Error = $"Duplicate plugin id '{PluginId}'.";

        _pluginsService.Plugins.Returns([loaded, duplicate]);
        _pluginsService.ReadEnabledIdsAsync().Returns(new HashSet<string> { loaded.Id });
        _pluginsService.ReadSettingsAsync(loaded.Id).Returns([]);
    }

    private void ConfirmDialogs()
        => _dialogs.ShowConfirmationAsync(
                Arg.Any<string>(), Arg.Any<Func<Task>>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<Action?>(), Arg.Any<Action?>())
            .Returns(async call => { await call.Arg<Func<Task>>()(); return true; });

    private const string ActionButtonSelector = ".kh-plugins-manager__actions .kh-button";

    [Fact]
    public void Row_DrawsThePluginsButtons_WithTheHandlersLabel()
    {
        var plugin = Plugin(PluginStatus.Loaded);
        _buttons.ButtonsFor(plugin.Id).Returns([(Button("session", "Sign in"), new PluginButtonState { Label = "Sign out" })]);
        Arrange(plugin, enabled: true);

        var cut = Render<PluginsManagerPage>();
        cut.Find(DisclosureSelector).Click();

        Assert.Equal("Sign out", cut.Find(ActionButtonSelector).TextContent.Trim());
    }

    [Fact]
    public void Button_Clicked_InvokesItThroughTheService()
    {
        var plugin = Plugin(PluginStatus.Loaded);
        _buttons.ButtonsFor(plugin.Id).Returns([(Button("session", "Sign in"), PluginButtonState.Default)]);
        Arrange(plugin, enabled: true);

        var cut = Render<PluginsManagerPage>();
        cut.Find(DisclosureSelector).Click();
        cut.Find(ActionButtonSelector).Click();

        _buttons.Received(1).InvokeAsync(plugin.Id, "session", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Button_ReportedDisabled_RendersDisabled()
    {
        var plugin = Plugin(PluginStatus.Loaded);
        _buttons.ButtonsFor(plugin.Id).Returns([(Button("session", "Sign in"), new PluginButtonState { Enabled = false })]);
        Arrange(plugin, enabled: true);

        var cut = Render<PluginsManagerPage>();
        cut.Find(DisclosureSelector).Click();

        Assert.True(cut.Find(ActionButtonSelector).HasAttribute("disabled"));
    }

    [Fact]
    public void Row_WithNoButtons_DrawsNoActionsBlock()
    {
        Arrange(Plugin(PluginStatus.Loaded), enabled: true);

        var cut = Render<PluginsManagerPage>();
        cut.Find(DisclosureSelector).Click();

        Assert.Empty(cut.FindAll(ActionButtonSelector));
    }

    private static PluginButtonDefinition Button(string key, string label, string? icon = null)
        => new() { Key = key, Label = label, Icon = icon };

    private void Arrange(DiscoveredPlugin plugin, bool enabled, Dictionary<string, JsonElement>? stored = null)
    {
        _pluginsService.Plugins.Returns([plugin]);
        _pluginsService.ReadEnabledIdsAsync().Returns(enabled ? new HashSet<string> { plugin.Id } : []);
        _pluginsService.ReadSettingsAsync(plugin.Id).Returns(stored ?? []);
    }

    private static DiscoveredPlugin Plugin(PluginStatus status, params PluginSettingDefinition[] settings)
        => Plugin(status, "test-plugin", settings);

    private static DiscoveredPlugin Plugin(PluginStatus status, string folderName, params PluginSettingDefinition[] settings) => new()
    {
        Directory = Path.Combine("plugins", folderName),
        Status = status,
        Manifest = new PluginManifest
        {
            Id = PluginId,
            Name = "Test Plugin",
            Version = "1.0.0",
            Description = "A plugin used by the tests.",
            EntryAssembly = "Test.dll",
            ApiVersion = PluginApi.CurrentVersion,
            Settings = [.. settings],
        },
    };

    private static PluginSettingDefinition Setting(
        string key, PluginSettingType type, string label, bool secret = false, string? section = null)
        => new() { Key = key, Type = type, Label = label, Secret = secret, Section = section };

    private static JsonElement Json<T>(T value) => JsonSerializer.SerializeToElement(value);

    // ── grouping settings under headings ───────────────────────────────────────────────

    /// <summary>Every manifest written before sections existed names none, and must render as one
    /// unheaded run exactly as it always did.</summary>
    [Fact]
    public void SettingsNamingNoSection_AreOneUnheadedRun()
    {
        var sections = PluginsManagerPage.SectionsOf([Field("a"), Field("b")]);

        var only = Assert.Single(sections);
        Assert.Null(only.Name);
        Assert.Equal(["a", "b"], only.Fields.Select(f => f.Definition.Key));
    }

    /// <summary>Declaration order decides heading order, so an author arranges the page by
    /// arranging the manifest rather than learning a second set of rules.</summary>
    [Fact]
    public void SectionsComeOutInTheOrderTheyAreFirstNamed()
    {
        var sections = PluginsManagerPage.SectionsOf(
            [Field("a", "Playback"), Field("b", "Bridge"), Field("c", "Playback")]);

        Assert.Equal(["Playback", "Bridge"], sections.Select(s => s.Name));

        // Gathered under their heading rather than repeating it, which is what makes interleaving
        // in the manifest harmless.
        Assert.Equal(["a", "c"], sections[0].Fields.Select(f => f.Definition.Key));
    }

    /// <summary>An ungrouped setting declared after a heading belongs with the ungrouped ones, not
    /// orphaned under a heading its author never named.</summary>
    [Fact]
    public void AnUngroupedSettingAfterAHeading_StaysWithTheUnheadedRun()
    {
        var sections = PluginsManagerPage.SectionsOf([Field("a", "Bridge"), Field("b")]);

        Assert.Equal([null, "Bridge"], sections.Select(s => s.Name));
        Assert.Equal(["b"], sections[0].Fields.Select(f => f.Definition.Key));
    }

    /// <summary>Blank is the same as absent: an empty heading would draw a rule with nothing
    /// above it, and a manifest carrying "" has not grouped anything.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankSectionName_IsNoSectionAtAll(string blank)
        => Assert.Null(Assert.Single(PluginsManagerPage.SectionsOf([Field("a", blank)])).Name);

    /// <summary>Matched the way every other name in this app is: a manifest spelling one heading
    /// two ways meant it once.</summary>
    [Fact]
    public void ASectionNamedInTwoCases_IsOneHeading()
    {
        var sections = PluginsManagerPage.SectionsOf([Field("a", "Bridge"), Field("b", "bridge")]);

        Assert.Single(sections);
        Assert.Equal(2, sections[0].Fields.Count);
    }

    /// <summary>The grouping is worth nothing if the page does not draw it.</summary>
    [Fact]
    public void ThePageDrawsAHeadingPerSection()
    {
        Arrange(
            Plugin(PluginStatus.Loaded,
                Setting("a", PluginSettingType.Int, "A", section: "Playback"),
                Setting("b", PluginSettingType.Int, "B", section: "Bridge")),
            enabled: true);

        var cut = Render<PluginsManagerPage>();
        cut.Find(DisclosureSelector).Click();

        Assert.Equal(["Playback", "Bridge"], cut.FindAll(SectionSelector).Select(e => e.TextContent.Trim()));
    }

    /// <summary>A plugin that groups nothing draws no headings at all, so nothing about the page
    /// changes for one that never adopts this.</summary>
    [Fact]
    public void APluginThatNamesNoSections_DrawsNoHeadings()
    {
        Arrange(Plugin(PluginStatus.Loaded, Setting("a", PluginSettingType.Int, "A")), enabled: true);

        var cut = Render<PluginsManagerPage>();
        cut.Find(DisclosureSelector).Click();

        Assert.Empty(cut.FindAll(SectionSelector));
        Assert.Single(cut.FindAll(SettingInputSelector));
    }

    private static PluginsManagerPage.SettingField Field(string key, string? section = null)
        => new() { Definition = Setting(key, PluginSettingType.Int, key, section: section) };


    // ── icons on a plugin's buttons ────────────────────────────────────────────────────

    /// <summary>Drawn before the label, with the bi- prefix the host adds rather than one the
    /// manifest repeats.</summary>
    [Fact]
    public void AButtonNamingAnIcon_DrawsItBeforeTheLabel()
    {
        Arrange(Plugin(PluginStatus.Loaded), enabled: true);
        _buttons.ButtonsFor(PluginId.ToString())
            .Returns([(Button("session", "Sign in", "box-arrow-in-right"), PluginButtonState.Default)]);

        var cut = Render<PluginsManagerPage>();
        cut.Find(DisclosureSelector).Click();

        var button = cut.Find(ActionButtonSelector);
        Assert.NotNull(button.QuerySelector("i.bi.bi-box-arrow-in-right"));
        Assert.Contains("Sign in", button.TextContent);
    }

    /// <summary>Every button written before icons existed names none, and must draw as it did:
    /// an empty glyph would leave a gap before the label on every one of them.</summary>
    [Fact]
    public void AButtonNamingNoIcon_DrawsNoGlyphAtAll()
    {
        Arrange(Plugin(PluginStatus.Loaded), enabled: true);
        _buttons.ButtonsFor(PluginId.ToString())
            .Returns([(Button("session", "Sign in"), PluginButtonState.Default)]);

        var cut = Render<PluginsManagerPage>();
        cut.Find(DisclosureSelector).Click();

        Assert.Empty(cut.FindAll(ActionButtonSelector + " i"));
    }

    /// <summary>The handler may rename a button per press; the icon is the manifest's and stays
    /// put, or a button would lose its glyph the moment it said something else.</summary>
    [Fact]
    public void AnOverriddenLabel_KeepsTheManifestsIcon()
    {
        Arrange(Plugin(PluginStatus.Loaded), enabled: true);
        _buttons.ButtonsFor(PluginId.ToString())
            .Returns([(Button("session", "Sign in", "box-arrow-in-right"), new PluginButtonState { Label = "Sign out" })]);

        var cut = Render<PluginsManagerPage>();
        cut.Find(DisclosureSelector).Click();

        var button = cut.Find(ActionButtonSelector);
        Assert.NotNull(button.QuerySelector("i.bi-box-arrow-in-right"));
        Assert.Contains("Sign out", button.TextContent);
    }


    /// <summary>One row at the foot: the folder it was loaded from holding the left edge, then
    /// every action held right, with the plugin's own buttons divided from the host's. The order
    /// is the point — the divider only reads as a boundary while it stands between the two.</summary>
    [Fact]
    public void APluginsButtons_SitRightOfTheFolder_DividedFromTheHostsControls()
    {
        Arrange(Plugin(PluginStatus.Loaded, Setting("a", PluginSettingType.Int, "A")), enabled: true);
        _buttons.ButtonsFor(PluginId.ToString())
            .Returns([(Button("session", "Sign in", "box-arrow-in-right"), PluginButtonState.Default)]);

        var cut = Render<PluginsManagerPage>();
        cut.Find(DisclosureSelector).Click();

        var foot = cut.Find(".kh-plugins-manager__foot");

        Assert.Equal(
        [
            "kh-plugins-manager__path",
            "kh-plugins-manager__actions",
            "kh-plugins-manager__divider",
            "kh-plugins-manager__foot-right",
        ], foot.Children.Select(child => child.ClassName?.Trim()));

        Assert.NotNull(foot.QuerySelector(".kh-plugins-manager__actions .kh-button"));
        Assert.NotNull(foot.QuerySelector(".kh-plugins-manager__foot-right .kh-button--outline-danger"));
    }

    /// <summary>A divider with nothing on its left is a rule floating beside the folder, so a
    /// plugin declaring no buttons draws neither.</summary>
    [Fact]
    public void ARowWithNoButtons_DrawsNoDivider()
    {
        Arrange(Plugin(PluginStatus.Loaded, Setting("a", PluginSettingType.Int, "A")), enabled: true);

        var cut = Render<PluginsManagerPage>();
        cut.Find(DisclosureSelector).Click();

        Assert.Empty(cut.FindAll(".kh-plugins-manager__divider"));
        Assert.NotNull(cut.Find(".kh-plugins-manager__foot .kh-plugins-manager__path"));
    }

}
