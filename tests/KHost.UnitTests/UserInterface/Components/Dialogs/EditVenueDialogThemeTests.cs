using Bunit;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services.Messaging;
using KHost.UserInterface.Components.Dialogs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using KHost.Domain.Services;
using KHost.Domain.Services.QrCodes;

namespace KHost.UnitTests.UserInterface.Components.Dialogs;

/// <summary>The venue's theme, and every screen colour that falls back to it until the venue picks its own.</summary>
public class EditVenueDialogThemeTests : BunitContext
{
    private const string SungWords = "#venue-colour-lyrics-sung";

    public EditVenueDialogThemeTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddAppSettings();

        var mediaPools = Substitute.For<IMediaPoolService>();
        mediaPools.ReadAllWithEntriesAsync(Arg.Any<PoolPurpose>(), Arg.Any<Guid?>()).Returns(new List<MediaPool>());
        var breakMusic = Substitute.For<IBreakMusicService>();
        breakMusic.Providers.Returns(new List<IBreakMusicProvider>());
        var visualisations = Substitute.For<IVisualisationPlaylistService>();
        visualisations.ReadAllWithEntriesAsync().Returns(new List<VisualisationPlaylist>());
        var plugins = Substitute.For<IPluginRegistry>();
        plugins.Plugins.Returns([]);

        Services.AddSingleton(breakMusic);
        Services.AddSingleton(Substitute.For<IQrCodePngExporter>());
        Services.AddSingleton(Substitute.For<IMediaUploader>());
        Services.AddSingleton(Substitute.For<IFlashService>());
        Services.AddSingleton(mediaPools);
        Services.AddSingleton(visualisations);
        Services.AddSingleton(Substitute.For<IMediaService>());
        Services.AddSingleton<IMessageBroker>(new MessageBroker(NullLogger<MessageBroker>.Instance));
        Services.AddSingleton(plugins);
    }

    private const string Preset = "#venue-theme-preset";

    [Fact]
    public void ThePresets_OfferNoneAndEveryReadyMadeTheme()
    {
        var cut = Render(new Venue.VenueSettings());

        Assert.Equal(["None", .. KHost.UserInterface.Models.VenueThemePresets.All.Select(p => p.Name)],
            cut.FindAll(Preset + " option").Select(o => o.TextContent));
    }

    [Fact]
    public void NoTheme_ThePresetReadsNone()
        => Assert.Equal("none", Render(new Venue.VenueSettings()).Find(Preset).GetAttribute("value"));

    /// <summary>Picking a preset fills the four theme colours, and every row that follows them moves too.</summary>
    [Fact]
    public void PickingAPreset_FillsTheThemeAndSavesIt()
    {
        Venue? saved = null;
        var cut = Render(new Venue.VenueSettings(), venue => saved = venue);

        cut.Find(Preset).Change("pub");

        Assert.Equal(("#1f8a4c", "#f2c14e", "#fff6e5", "#0d1a12"),
            (cut.Find("#venue-theme-primary").GetAttribute("value"), cut.Find("#venue-theme-highlight").GetAttribute("value"),
             cut.Find("#venue-theme-text").GetAttribute("value"), cut.Find("#venue-theme-shadow").GetAttribute("value")));
        Assert.Equal("#f2c14e", cut.Find(SungWords).GetAttribute("value"));
        cut.Find("form").Submit();
        Assert.Equal(("#1f8a4c", "#f2c14e", "#fff6e5", "#0d1a12"),
            (saved!.Settings.ThemePrimaryColor, saved.Settings.ThemeHighlightColor, saved.Settings.ThemeTextColor, saved.Settings.ThemeShadowColor));
    }

    /// <summary>Nothing records the pick: a venue whose four colours are a preset's reads as that preset.</summary>
    [Fact]
    public void ThemeColoursMatchingAPreset_ReadAsThatPreset()
    {
        var cut = Render(new Venue.VenueSettings
        {
            ThemePrimaryColor = "#6C63FF", ThemeHighlightColor = "#c8b6ff", ThemeTextColor = "#ecebff", ThemeShadowColor = "#0b0a1a",
        });

        Assert.Equal("midnight", cut.Find(Preset).GetAttribute("value"));
    }

    [Fact]
    public void ChangingAColourAfterAPreset_ReadsAsCustom()
    {
        var cut = Render(new Venue.VenueSettings());
        cut.Find(Preset).Change("pub");

        cut.Find("#venue-theme-text").Change("#000001");

        Assert.Equal("custom", cut.Find(Preset).GetAttribute("value"));
        Assert.Equal("Custom", cut.Find(Preset + " option").TextContent);
    }

    [Theory]
    [InlineData("#6c63fe", "#c8b6ff", "#ecebff", "#0b0a1a")]
    [InlineData("#6c63ff", "#c8b6fe", "#ecebff", "#0b0a1a")]
    [InlineData("#6c63ff", "#c8b6ff", "#ecebfe", "#0b0a1a")]
    [InlineData("#6c63ff", "#c8b6ff", "#ecebff", "#0b0a1b")]
    public void ThemeColoursOneAwayFromAPreset_ReadAsCustom(string primary, string highlight, string text, string shadow)
    {
        var cut = Render(new Venue.VenueSettings
        {
            ThemePrimaryColor = primary, ThemeHighlightColor = highlight, ThemeTextColor = text, ThemeShadowColor = shadow,
        });

        Assert.Equal("custom", cut.Find(Preset).GetAttribute("value"));
    }

    /// <summary>"Custom" only names the colours as they stand; choosing it again changes none of them.</summary>
    [Fact]
    public void ChoosingCustomAgain_KeepsTheColours()
    {
        var cut = Render(new Venue.VenueSettings { ThemePrimaryColor = "#123456" });

        cut.Find(Preset).Change("custom");

        Assert.Equal(("#123456", "custom"), (cut.Find("#venue-theme-primary").GetAttribute("value"), cut.Find(Preset).GetAttribute("value")));
    }

    [Fact]
    public void PickingNone_ClearsTheTheme()
    {
        Venue? saved = null;
        var cut = Render(new Venue.VenueSettings { ThemePrimaryColor = "#111111", ThemeShadowColor = "#222222" }, venue => saved = venue);

        cut.Find(Preset).Change("none");
        cut.Find("form").Submit();

        Assert.Equal(((string?)null, (string?)null, (string?)null, (string?)null),
            (saved!.Settings.ThemePrimaryColor, saved.Settings.ThemeHighlightColor, saved.Settings.ThemeTextColor, saved.Settings.ThemeShadowColor));
    }

    /// <summary>No theme and nothing of its own: the row says so, and its picker shows, without
    /// taking, the colour the screen draws.</summary>
    [Fact]
    public void NoTheme_ARowLeftUnset_ShowsTheScreensOwnColourAndTakesNothing()
    {
        var cut = Render(new Venue.VenueSettings());

        var picker = cut.Find(SungWords);
        Assert.Equal("#8558fa", picker.GetAttribute("value"));
        Assert.Empty(cut.FindAll(SungWords + "-clear"));
        Assert.Contains("the screen's own colour", Row(cut, SungWords).TextContent);
    }

    /// <summary>A theme colour shows on every row that falls back to it, named as the source.</summary>
    [Fact]
    public void ThemeSet_ARowLeftUnset_FollowsTheTheme()
    {
        var cut = Render(new Venue.VenueSettings { ThemeHighlightColor = "#ff0000" });

        Assert.Equal("#ff0000", cut.Find(SungWords).GetAttribute("value"));
        Assert.Contains("the theme's highlight", Row(cut, SungWords).TextContent);
    }

    /// <summary>Changing the theme in the dialog moves the rows that follow it at once.</summary>
    [Fact]
    public void SettingAThemeColour_MovesTheRowsThatFollowIt()
    {
        var cut = Render(new Venue.VenueSettings());

        cut.Find("#venue-theme-highlight").Change("#00ff00");

        Assert.Equal("#00ff00", cut.Find(SungWords).GetAttribute("value"));
        Assert.Single(cut.FindAll("#venue-theme-highlight-clear"));
        Assert.Empty(cut.FindAll(SungWords + "-clear"));
    }

    [Fact]
    public void ThemeAndOwnColours_ReachTheSavedVenue()
    {
        Venue? saved = null;
        var cut = Render(new Venue.VenueSettings(), venue => saved = venue);

        cut.Find("#venue-theme-primary").Change("#123456");
        cut.Find("#venue-colour-qr-frame").Change("#fefefe");
        cut.Find("form").Submit();

        Assert.Equal(("#123456", "#fefefe"), (saved!.Settings.ThemePrimaryColor, saved.Settings.QrCodeFrameColor));
    }

    /// <summary>Clearing a colour hands it back to the theme: nothing is stored for it.</summary>
    [Fact]
    public void ClearingAnOwnColour_SavesNothingForIt()
    {
        Venue? saved = null;
        var cut = Render(new Venue.VenueSettings { LyricsSungColor = "#abcdef" }, venue => saved = venue);

        Assert.Equal("#abcdef", cut.Find(SungWords).GetAttribute("value"));
        cut.Find(SungWords + "-clear").Click();

        Assert.Equal(("#8558fa", 0), (cut.Find(SungWords).GetAttribute("value"), cut.FindAll(SungWords + "-clear").Count));
        cut.Find("form").Submit();

        Assert.Null(saved!.Settings.LyricsSungColor);
    }

    /// <summary>A colour left to the theme stays left to it through a save, so a later theme still reaches it.</summary>
    [Fact]
    public void SavingWithoutTouchingAColour_KeepsItFollowingTheTheme()
    {
        Venue? saved = null;
        var cut = Render(new Venue.VenueSettings { ThemeTextColor = "#eeeeee" }, venue => saved = venue);

        cut.Find("form").Submit();

        Assert.Equal(("#eeeeee", (string?)null), (saved!.Settings.ThemeTextColor, saved.Settings.LyricsUnsungColor));
    }

    /// <summary>The marquee's colours follow the theme the same way.</summary>
    [Fact]
    public void TheMarquee_LeftUnset_FollowsTheTheme()
    {
        var cut = Render(new Venue.VenueSettings { MarqueeEnabled = true, ThemeShadowColor = "#202020", ThemePrimaryColor = "#303030" });

        Assert.Equal("#202020", cut.Find("#marquee-background").GetAttribute("value"));
        Assert.Empty(cut.FindAll("#marquee-background-clear"));
        Assert.Equal("#303030", cut.Find("#marquee-divider-color").GetAttribute("value"));
    }

    /// <summary>Singers take the primary and songs the highlight, so the two read apart from each other.</summary>
    [Fact]
    public void TheMarqueesSingersAndSongs_LeftUnset_TakePrimaryAndHighlight()
    {
        var cut = Render(new Venue.VenueSettings { MarqueeEnabled = true, ThemePrimaryColor = "#303030", ThemeHighlightColor = "#404040" });

        Assert.Equal(("#303030", "#404040"),
            (cut.Find("#marquee-singer-color").GetAttribute("value"), cut.Find("#marquee-song-color").GetAttribute("value")));
    }

    /// <summary>With no theme, singers and songs show the screen's own colours, not the band's text, so
    /// changing the text moves neither.</summary>
    [Fact]
    public void NoTheme_TheMarqueesSingersAndSongs_KeepTheScreensOwnWhenTheTextChanges()
    {
        var cut = Render(new Venue.VenueSettings { MarqueeEnabled = true });

        cut.Find("#marquee-text").Change("#123123");

        Assert.Equal(("#8558fa", "#33ccff"),
            (cut.Find("#marquee-singer-color").GetAttribute("value"), cut.Find("#marquee-song-color").GetAttribute("value")));
    }

    /// <summary>The dialog once stored the screen's own marquee colours on every save; read back
    /// they are taken as unset, so that venue follows its theme.</summary>
    [Theory]
    [InlineData("#000000", "#f2f2f5")]
    [InlineData("#000000", "#F2F2F5")]
    public void AnOldDefaultMarqueeColour_ReadsAsUnset(string background, string text)
    {
        Venue? saved = null;
        var cut = Render(new Venue.VenueSettings
        {
            MarqueeEnabled = true,
            MarqueeBackgroundColor = background,
            MarqueeTextColor = text,
            MarqueeSingerColor = text,
        }, venue => saved = venue);

        Assert.Empty(cut.FindAll("#marquee-background-clear"));
        cut.Find("form").Submit();

        Assert.Equal(((string?)null, (string?)null, (string?)null),
            (saved!.Settings.MarqueeBackgroundColor, saved.Settings.MarqueeTextColor, saved.Settings.MarqueeSingerColor));
    }

    /// <summary>With no theme the screen draws the divider in the band's text, faintly, so the row follows that text.</summary>
    [Fact]
    public void NoTheme_TheMarqueesDivider_FollowsItsText()
    {
        var cut = Render(new Venue.VenueSettings());

        cut.Find("#marquee-text").Change("#123123");

        Assert.Equal("#123123", cut.Find("#marquee-divider-color").GetAttribute("value"));
    }

    /// <summary>Each run of screen colours sits under its own heading, which says when it applies.</summary>
    [Fact]
    public void TheLyricColours_SitUnderTheirOwnHeadingSayingWhenTheyApply()
    {
        var cut = Render(new Venue.VenueSettings());

        var group = cut.Find(SungWords).Closest(".kh-venue-settings-group")!;
        Assert.Equal(("Lyrics", "Only where the song sets none"),
            (group.QuerySelector(".kh-venue-settings-group__title")!.TextContent, group.QuerySelector(".kh-venue-settings-group__note")!.TextContent));
        Assert.Equal(3, group.QuerySelectorAll(".kh-venue-settings-group__rows input[type=color]").Length);
    }

    private static AngleSharp.Dom.IElement Row(IRenderedComponent<EditVenueDialog> cut, string pickerSelector)
        => cut.Find(pickerSelector).Closest(".kh-venue-settings__row")!;

    private IRenderedComponent<EditVenueDialog> Render(Venue.VenueSettings settings, Action<Venue>? onSave = null)
        => Render<EditVenueDialog>(ps => ps
            .Add(p => p.IsOpen, true)
            .Add(p => p.Venue, new Venue { Name = "Test Venue", Settings = settings })
            .Add(p => p.OnSave, venue => onSave?.Invoke(venue)));
}
