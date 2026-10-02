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

        _settings.Current.Returns(_ => _stored with { });
        _settings.DefaultMediaDirectory.Returns("/karaoke");
        _settings.SaveAsync(Arg.Any<AppSettings>()).Returns(new AppSettingsSaveResult(true));

        Services.AddSingleton(_settings);
        Services.AddSingleton(Substitute.For<IDialogService>());
        Services.AddSingleton(Substitute.For<IFlashService>());
        Services.AddSingleton(Substitute.For<IUsersService>());
        Services.AddFFmpegSection();
        Services.AddSearchSection();
    }

    [Fact]
    public void Render_EachSection_IsItsOwnPanelInsideOneGridContainer()
    {
        var page = Render<AppSettingsPage>();

        var grid = page.Find(".kh-app-settings__grid");
        var panels = grid.QuerySelectorAll(":scope > .kh-app-settings__panel");

        // Screens, Playback, Ads, Pagination, FFmpeg, Media, Search: one panel each, not folded
        // two-to-a-card, and every one is a direct child of the grid so the CSS grid actually
        // lays them out rather than a wrapper it never sees. No Security panel: sign-in is a
        // config-only flag now, not something this page saves.
        Assert.Equal(7, panels.Length);
        Assert.All(panels, panel => Assert.Contains("kh-card", panel.ClassList));

        var titles = panels
            .Select(panel => panel.QuerySelector(".kh-card__title")?.TextContent.Trim())
            .ToList();
        Assert.Equal(
            ["Screens", "Playback", "Ads", "Pagination", "FFmpeg", "Media", "Search"],
            titles);
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
