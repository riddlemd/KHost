using Bunit;
using KHost.Abstractions.Services;
using KHost.UserInterface.Components.Pages.Settings;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;

namespace KHost.UnitTests.UserInterface.Components.Pages.Settings;

/// <summary>TODO 68: the page became a full-width, multi-column grid of per-section panels
/// instead of one narrow card. These pin the grid itself, so a merge of two sections back into
/// one panel or a dropped grid container goes red rather than silently reverting the layout.</summary>
public class AppSettingsPageLayoutTests : BunitContext
{
    // Every element id the pre-grid markup carried, gathered from the other AppSettingsPage test
    // fixtures: a rework that drops one still compiles and still saves, so nothing else catches it.
    private static readonly string[] ExpectedIds =
    [
        "launch-screen-on-startup",
        "graphics-scale",
        "dynamic-lead-ins",
        "dynamic-lead-in-pause",
        "lead-in-grace",
        "colour-blind-lyrics",
        "default-search-mode",
    ];

    private readonly IAppSettingsService _settings = Substitute.For<IAppSettingsService>();
    private readonly AppSettings _stored = new();

    public AppSettingsPageLayoutTests()
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
        Services.AddBreakMusicSection();
    }

    /// <summary>Framed like every other settings page: one card titled with its glyph, the
    /// section panels and the Save button inside it.</summary>
    [Fact]
    public void Render_ThePage_IsOneTitledCardHoldingThePanelsAndSave()
    {
        var page = Render<AppSettingsPage>();

        var card = page.Find(".kh-settings-page > .kh-card.kh-app-settings");
        var title = card.QuerySelector(":scope > .kh-card__header .kh-card__title")!;
        Assert.Equal("App Settings", title.TextContent.Trim());
        Assert.NotNull(title.QuerySelector(".bi-gear-fill"));

        var body = card.QuerySelector(":scope > .kh-card__body")!;
        Assert.NotNull(body.QuerySelector(":scope > .kh-app-settings__grid"));
        Assert.NotNull(body.QuerySelector(":scope > .kh-app-settings__actions button"));
    }

    [Fact]
    public void Render_EachSection_IsItsOwnPanelInsideOneGridContainer()
    {
        var page = Render<AppSettingsPage>();

        var grid = page.Find(".kh-app-settings__grid");
        var panels = grid.QuerySelectorAll(":scope > .kh-app-settings__panel");

        // Screens, Playback, Lyrics, Break music, New venues, Ads, Console, Pagination, Media, FFmpeg, Video: one panel each, not folded
        // two-to-a-card, and every one is a direct child of the grid so the CSS grid actually
        // lays them out rather than a wrapper it never sees. No Security panel: sign-in is a
        // config-only flag now, not something this page saves.
        Assert.Equal(11, panels.Length);
        Assert.All(panels, panel => Assert.Contains("kh-card", panel.ClassList));

        var titles = panels
            .Select(panel => panel.QuerySelector(".kh-card__title")?.TextContent.Trim())
            .ToList();
        Assert.Equal(
            ["Screens", "Playback", "Lyrics", "Break music", "New venues", "Ads", "Console", "Pagination", "Media", "FFmpeg", "Video"],
            titles);
    }

    /// <summary>Every lyric setting reaches timed lyrics alone, so the panel says so once, above its
    /// rows, rather than in each row's note.</summary>
    [Fact]
    public void Lyrics_SaysOnceAboveItsRows_ThatOnlyTimedLyricsAreAffected()
    {
        var page = Render<AppSettingsPage>();
        var lyrics = page.FindAll(".kh-app-settings__panel").Single(p => p.QuerySelector(".kh-card__title")!.TextContent.Trim() == "Lyrics");
        var body = lyrics.QuerySelector(".kh-card__body")!;

        Assert.Contains("timed lyrics", body.FirstElementChild!.TextContent);
        Assert.Contains("kh-app-settings__intro", body.FirstElementChild.ClassList);
        var notes = body.QuerySelectorAll(".kh-app-settings__description .kh-note");
        Assert.NotEmpty(notes);
        Assert.All(notes, note => Assert.DoesNotContain("CD+G", note.TextContent));
    }

    /// <summary>The unit sits in the box beside the value, not in the label.</summary>
    [Fact]
    public void BackingVocalVolume_CarriesItsPercentInTheBox_NotTheLabel()
    {
        var page = Render<AppSettingsPage>();
        var input = page.Find("input#backing-vocal-volume");
        var row = input.Closest(".kh-app-settings__row")!;

        Assert.Equal("%", input.Closest(".kh-input-affix")!.QuerySelector(".kh-input-affix__text--suffix")!.TextContent);
        Assert.DoesNotContain("(%)", row.QuerySelector(".kh-app-settings__labelled")!.TextContent);
        Assert.Contains("Default backing vocals volume", row.TextContent);
    }

    [Fact]
    public void Render_EveryPreviouslyPresentFieldId_IsStillInTheGrid()
    {
        var page = Render<AppSettingsPage>();

        var grid = page.Find(".kh-app-settings__grid");

        foreach (var id in ExpectedIds)
            Assert.NotNull(grid.QuerySelector($"#{id}"));
    }
}
