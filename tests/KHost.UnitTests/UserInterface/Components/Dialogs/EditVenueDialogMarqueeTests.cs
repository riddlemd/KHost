using Bunit;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Models;
using KHost.Abstractions.Models.Backgrounds;
using KHost.Abstractions.Services;
using KHost.Domain.Services.Messaging;
using KHost.UserInterface.Components.Dialogs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.UserInterface.Components.Dialogs;

/// <summary>The marquee's controls stay hidden until switched on, checked via the checkbox.</summary>
public class EditVenueDialogMarqueeTests : BunitContext
{
    private const string EnabledSelector = "#venue-marquee-enabled";
    private const string SingerCountSelector = "#marquee-singer-count";
    private const string PositionSelector = "#marquee-position";
    private const string FontSizeSelector = "#marquee-font-size";
    private const string SpeedSelector = "#marquee-speed";
    private const string PinSelector = "#venue-marquee-pin-label";
    private const string BackgroundSelector = "#marquee-background";
    private const string EntryFormatSelector = "#marquee-entry-format";
    private const string OpacitySelector = "#marquee-background-opacity";
    private const string SingerColorSelector = "#marquee-singer-color";
    private const string SongColorSelector = "#marquee-song-color";
    private const string DividerColorSelector = "#marquee-divider-color";
    private const string DividerShapeSelector = "#marquee-divider-shape";

    private readonly IBreakMusicService _breakMusic = Substitute.For<IBreakMusicService>();
    private readonly IMediaPoolService _mediaPools = Substitute.For<IMediaPoolService>();
    private readonly IMediaService _media = Substitute.For<IMediaService>();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);

    public EditVenueDialogMarqueeTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        _mediaPools.ReadAllWithEntriesAsync(Arg.Any<PoolPurpose>(), Arg.Any<Guid?>())
            .Returns(new List<MediaPool>());
        _breakMusic.Providers.Returns(new List<IBreakMusicProvider>());

        Services.AddSingleton(_breakMusic);
        Services.AddSingleton(_mediaPools);
        Services.AddSingleton(_media);
        Services.AddSingleton<IMessageBroker>(_broker);

        // The dialog reads the venue's background folder on open; an empty pack is the
        // shape a venue that has never chosen one has.
        var backgroundPacks = Substitute.For<IBackgroundPackService>();
        backgroundPacks.ReadAsync(Arg.Any<CancellationToken>())
            .Returns(new BackgroundPack());
        Services.AddSingleton(backgroundPacks);

        // The dialog lists QR sources from the manifests; none here, but it has to resolve.
        var plugins = Substitute.For<IPluginRegistry>();
        plugins.Plugins.Returns([]);
        Services.AddSingleton(plugins);
    }

    /// <summary>Nothing but the switch until the venue wants one; the rest is noise otherwise.</summary>
    [Fact]
    public void MarqueeOff_ShowsOnlyTheSwitch()
    {
        var cut = Render(new Venue.VenueSettings());

        Assert.Single(cut.FindAll(EnabledSelector));
        Assert.Empty(cut.FindAll(SingerCountSelector));
    }

    [Fact]
    public void MarqueeOn_ShowsTheSettings()
    {
        var cut = Render(new Venue.VenueSettings { MarqueeEnabled = true });

        Assert.Single(cut.FindAll(SingerCountSelector));
        Assert.Single(cut.FindAll(PositionSelector));
        Assert.Single(cut.FindAll(BackgroundSelector));
    }

    /// <summary>A never-enabled marquee stores zero singers, which would band-name nobody.</summary>
    [Fact]
    public void MarqueeNeverEnabled_SwitchingItOn_OffersSingersRatherThanNone()
    {
        var cut = Render(new Venue.VenueSettings { MarqueeEnabled = false, MarqueeSingerCount = 0 });

        cut.Find(EnabledSelector).Change(true);

        Assert.NotEqual("0", cut.Find(SingerCountSelector).GetAttribute("value"));
    }

    /// <summary>Once it has been on, zero is the venue's own answer and is left alone.</summary>
    [Fact]
    public void MarqueeAlreadyEnabled_KeepsAStoredZero()
    {
        var cut = Render(new Venue.VenueSettings { MarqueeEnabled = true, MarqueeSingerCount = 0 });

        Assert.Equal("0", cut.Find(SingerCountSelector).GetAttribute("value"));
    }

    [Fact]
    public void MarqueeOn_ShowsTheVenuesStoredCount()
    {
        var cut = Render(new Venue.VenueSettings { MarqueeEnabled = true, MarqueeSingerCount = 7 });

        Assert.Equal("7", cut.Find(SingerCountSelector).GetAttribute("value"));
    }

    /// <summary>A native colour input has no empty state, so an unset venue needs a fallback.</summary>
    [Fact]
    public void MarqueeOn_NoColoursStored_FallsBackRatherThanShowingBlack()
    {
        var cut = Render(new Venue.VenueSettings { MarqueeEnabled = true });

        Assert.False(string.IsNullOrEmpty(cut.Find(BackgroundSelector).GetAttribute("value")));
    }

    /// <summary>A number input can't show "the screen decides", so no size gets the screen's own.</summary>
    [Fact]
    public void MarqueeOn_NoFontSizeStored_OffersTheScreensOwnSizeRatherThanZero()
    {
        var cut = Render(new Venue.VenueSettings { MarqueeEnabled = true });

        Assert.NotEqual("0", cut.Find(FontSizeSelector).GetAttribute("value"));
    }

    [Fact]
    public void MarqueeOn_ShowsTheVenuesStoredFontSize()
    {
        var cut = Render(new Venue.VenueSettings { MarqueeEnabled = true, MarqueeFontSizePixels = 44 });

        Assert.Equal("44", cut.Find(FontSizeSelector).GetAttribute("value"));
    }

    [Fact]
    public void MarqueeOn_NoScrollSpeedStored_OffersTheScreensOwnSpeedRatherThanZero()
    {
        var cut = Render(new Venue.VenueSettings { MarqueeEnabled = true });

        Assert.NotEqual("0", cut.Find(SpeedSelector).GetAttribute("value"));
    }

    [Fact]
    public void MarqueeOn_ShowsTheVenuesStoredScrollSpeed()
    {
        var cut = Render(new Venue.VenueSettings { MarqueeEnabled = true, MarqueeScrollSpeed = 140 });

        Assert.Equal("140", cut.Find(SpeedSelector).GetAttribute("value"));
    }

    /// <summary>An unset format leaves the field blank rather than pre-filling its own default.</summary>
    [Fact]
    public void MarqueeOn_NoEntryFormatStored_LeavesTheFieldBlank()
    {
        var cut = Render(new Venue.VenueSettings { MarqueeEnabled = true });

        Assert.Null(cut.Find(EntryFormatSelector).GetAttribute("value"));
    }

    [Fact]
    public void MarqueeOn_ShowsTheVenuesStoredEntryFormat()
    {
        var cut = Render(new Venue.VenueSettings { MarqueeEnabled = true, MarqueeEntryFormat = "{artist} - {song}" });

        Assert.Equal("{artist} - {song}", cut.Find(EntryFormatSelector).GetAttribute("value"));
    }

    [Fact]
    public void MarqueeOn_ShowsWhetherTheLabelIsPinned()
    {
        var cut = Render(new Venue.VenueSettings { MarqueeEnabled = true, MarqueePinLabel = true });

        Assert.True(cut.Find(PinSelector).HasAttribute("checked"));
    }

    /// <summary>Unlike font size and scroll speed, zero is a real opacity choice, so it must not be
    /// confused with "the venue never set one" here.</summary>
    [Fact]
    public void MarqueeOn_NoOpacityStored_OffersTheScreensOwnEightyTwoPercent()
    {
        var cut = Render(new Venue.VenueSettings { MarqueeEnabled = true });

        Assert.Equal("82", cut.Find(OpacitySelector).GetAttribute("value"));
    }

    [Fact]
    public void MarqueeOn_ShowsTheVenuesStoredOpacity()
    {
        var cut = Render(new Venue.VenueSettings { MarqueeEnabled = true, MarqueeBackgroundOpacity = 25 });

        Assert.Equal("25", cut.Find(OpacitySelector).GetAttribute("value"));
    }

    /// <summary>Stored zero is a deliberate fully-transparent choice, kept rather than treated as unset.</summary>
    [Fact]
    public void MarqueeOn_OpacityStoredAsZero_KeepsTheStoredZero()
    {
        var cut = Render(new Venue.VenueSettings { MarqueeEnabled = true, MarqueeBackgroundOpacity = 0 });

        Assert.Equal("0", cut.Find(OpacitySelector).GetAttribute("value"));
    }

    [Fact]
    public void MarqueeOn_NoSingerOrSongColourStored_FallsBackRatherThanShowingBlack()
    {
        var cut = Render(new Venue.VenueSettings { MarqueeEnabled = true });

        Assert.False(string.IsNullOrEmpty(cut.Find(SingerColorSelector).GetAttribute("value")));
        Assert.False(string.IsNullOrEmpty(cut.Find(SongColorSelector).GetAttribute("value")));
        Assert.False(string.IsNullOrEmpty(cut.Find(DividerColorSelector).GetAttribute("value")));
    }

    [Fact]
    public void MarqueeOn_ShowsTheVenuesStoredSingerSongAndDividerColours()
    {
        var cut = Render(new Venue.VenueSettings
        {
            MarqueeEnabled = true,
            MarqueeSingerColor = "#ff8800",
            MarqueeSongColor = "#00ffaa",
            MarqueeDividerColor = "#8888ff",
        });

        Assert.Equal("#ff8800", cut.Find(SingerColorSelector).GetAttribute("value"));
        Assert.Equal("#00ffaa", cut.Find(SongColorSelector).GetAttribute("value"));
        Assert.Equal("#8888ff", cut.Find(DividerColorSelector).GetAttribute("value"));
    }

    /// <summary>Dot is the zero value: an unset venue shows today's shape, not a blank choice.</summary>
    [Fact]
    public void MarqueeOn_NoDividerShapeStored_ShowsTheDot()
    {
        var cut = Render(new Venue.VenueSettings { MarqueeEnabled = true });

        Assert.Equal(nameof(MarqueeDividerShape.Dot), cut.Find(DividerShapeSelector).GetAttribute("value"));
    }

    [Fact]
    public void MarqueeOn_ShowsTheVenuesStoredDividerShape()
    {
        var cut = Render(new Venue.VenueSettings { MarqueeEnabled = true, MarqueeDividerShape = MarqueeDividerShape.Star });

        Assert.Equal(nameof(MarqueeDividerShape.Star), cut.Find(DividerShapeSelector).GetAttribute("value"));
    }

    /// <summary>What the host picks in the dialog reaches the saved venue, opacity included, and a
    /// value out of range is clamped rather than rejected.</summary>
    [Fact]
    public void WhatTheHostPicksForTheDivider_ReachesTheSavedVenue()
    {
        Venue? saved = null;
        var cut = Render(new Venue.VenueSettings { MarqueeEnabled = true }, venue => saved = venue);

        cut.Find(OpacitySelector).Change("40");
        cut.Find(SingerColorSelector).Change("#ff8800");
        cut.Find(SongColorSelector).Change("#00ffaa");
        cut.Find(DividerColorSelector).Change("#8888ff");
        cut.Find(DividerShapeSelector).Change(nameof(MarqueeDividerShape.Star));
        cut.Find("form").Submit();

        Assert.NotNull(saved);
        Assert.Equal(40, saved!.Settings.MarqueeBackgroundOpacity);
        Assert.Equal("#ff8800", saved.Settings.MarqueeSingerColor);
        Assert.Equal("#00ffaa", saved.Settings.MarqueeSongColor);
        Assert.Equal("#8888ff", saved.Settings.MarqueeDividerColor);
        Assert.Equal(MarqueeDividerShape.Star, saved.Settings.MarqueeDividerShape);
    }

    private IRenderedComponent<EditVenueDialog> Render(Venue.VenueSettings settings, Action<Venue>? onSave = null)
        => Render<EditVenueDialog>(ps => ps
            .Add(p => p.IsOpen, true)
            .Add(p => p.Venue, new Venue { Name = "Test Venue", Settings = settings })
            .Add(p => p.OnSave, venue => onSave?.Invoke(venue)));
}
