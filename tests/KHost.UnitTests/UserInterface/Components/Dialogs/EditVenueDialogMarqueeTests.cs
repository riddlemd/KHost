using Bunit;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services.Messaging;
using KHost.UserInterface.Components.Dialogs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.UserInterface.Components.Dialogs;

/// <summary>
/// The marquee's controls are hidden until it is switched on, so the section has to be checked
/// through the checkbox rather than by rendering and reading the inputs.
/// </summary>
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
    }

    /// <summary>Nothing but the switch until the venue wants one — the rest is noise otherwise.</summary>
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

    /// <summary>
    /// A venue that has never had a marquee stores zero singers, and offering that back is a band
    /// that names nobody — the one thing the host just asked for. The suggestion stands until the
    /// marquee has been on once.
    /// </summary>
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

    /// <summary>A native colour input has no empty state, so a venue with none set must be given one.</summary>
    [Fact]
    public void MarqueeOn_NoColoursStored_FallsBackRatherThanShowingBlack()
    {
        var cut = Render(new Venue.VenueSettings { MarqueeEnabled = true });

        Assert.False(string.IsNullOrEmpty(cut.Find(BackgroundSelector).GetAttribute("value")));
    }

    /// <summary>
    /// A number input cannot show "the screen decides", so a venue that has chosen no size is
    /// offered the size the screen would pick — saving that back changes nothing on screen.
    /// </summary>
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

    /// <summary>An unset format leaves the field blank rather than pre-filling the default it falls back to.</summary>
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

    private IRenderedComponent<EditVenueDialog> Render(Venue.VenueSettings settings)
        => Render<EditVenueDialog>(ps => ps
            .Add(p => p.IsOpen, true)
            .Add(p => p.Venue, new Venue { Name = "Test Venue", Settings = settings }));
}
