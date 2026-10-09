using Bunit;
using KHost.Abstractions.Services;
using KHost.UserInterface.Components.Pages.Settings;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;

namespace KHost.UnitTests.UserInterface.Components.Pages.Settings;

/// <summary>The break music mode is one for every venue, picked under App Settings.</summary>
public class AppSettingsPageBreakMusicTests : BunitContext
{
    private readonly IAppSettingsService _settings = Substitute.For<IAppSettingsService>();
    private AppSettings _stored = new() { BreakMusicProvider = "LibraryBreakMusicProvider" };

    public AppSettingsPageBreakMusicTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddNewVenuesSection();

        _settings.Current.Returns(_ => _stored with { });
        _settings.DefaultMediaDirectory.Returns("/karaoke");
        _settings.SaveAsync(Arg.Any<AppSettings>()).Returns(new AppSettingsSaveResult(true));

        Services.AddSingleton(_settings);
        Services.AddSingleton(Substitute.For<IDialogService>());
        Services.AddSingleton(Substitute.For<IFlashService>());
        Services.AddSingleton(Substitute.For<IUsersService>());
        Services.AddFFmpegSection();
        Services.AddSearchSection();
        Services.AddBreakMusicSection(
            AppSettingsPageServices.FakeBreakMusicProvider("LibraryBreakMusicProvider", "Local"),
            AppSettingsPageServices.FakeBreakMusicProvider("JukeboxProvider", "Jukebox"));
    }

    [Fact]
    public void PlaysFrom_ListsEveryLoadedMode_WithTheStoredOneChosen()
    {
        var page = Render<AppSettingsPage>();

        var options = page.FindAll("select#break-music-provider option");

        Assert.Equal(
            [("LibraryBreakMusicProvider", "Local playlist"), ("JukeboxProvider", "Jukebox")],
            options.Select(o => (o.GetAttribute("value"), o.TextContent.Trim())));
        Assert.Equal("LibraryBreakMusicProvider", page.Find("select#break-music-provider").GetAttribute("value"));
    }

    /// <summary>A plugin that failed to load leaves its name stored; the select must still carry it,
    /// or saving any other setting would quietly switch the room to something else.</summary>
    [Fact]
    public void PlaysFrom_TheStoredModeIsNotLoaded_KeepsItAsAChoiceThatSaysSo()
    {
        _stored = _stored with { BreakMusicProvider = "SpotifyBreakMusicProvider" };

        var page = Render<AppSettingsPage>();

        var first = page.FindAll("select#break-music-provider option")[0];
        Assert.Equal("SpotifyBreakMusicProvider", first.GetAttribute("value"));
        Assert.Contains("not loaded", first.TextContent);
        Assert.Equal("SpotifyBreakMusicProvider", page.Find("select#break-music-provider").GetAttribute("value"));
    }

    /// <summary>Stored names are a key matched however they were cased.</summary>
    [Fact]
    public void PlaysFrom_TheStoredModeDiffersOnlyByCase_AddsNoExtraChoice()
    {
        _stored = _stored with { BreakMusicProvider = "JUKEBOXPROVIDER" };

        Assert.Equal(2, Render<AppSettingsPage>().FindAll("select#break-music-provider option").Count);
    }

    [Fact]
    public async Task PlaysFrom_Picked_IsWhatSaveWrites()
    {
        var page = Render<AppSettingsPage>();

        page.Find("select#break-music-provider").Change("JukeboxProvider");
        await page.Find(".kh-app-settings__actions button").ClickAsync(new());

        await _settings.Received(1).SaveAsync(Arg.Is<AppSettings>(s => s.BreakMusicProvider == "JukeboxProvider"));
    }
}
