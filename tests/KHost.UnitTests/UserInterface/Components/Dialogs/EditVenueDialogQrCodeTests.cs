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
/// These three settings decide where a QR code goes when a plugin puts one on the screen. Nothing
/// else could set them — they are a JSON column with no other surface — so the dialog is the whole
/// of whether the feature is reachable.
/// </summary>
public class EditVenueDialogQrCodeTests : BunitContext
{
    private const string CornerSelector = "#venue-qr-corner";
    private const string SizeSelector = "#venue-qr-size";
    private const string HideSelector = "#venue-qr-hide-during-song";

    private readonly IBreakMusicService _breakMusic = Substitute.For<IBreakMusicService>();
    private readonly IMediaPoolService _mediaPools = Substitute.For<IMediaPoolService>();
    private readonly IMediaService _media = Substitute.For<IMediaService>();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);

    public EditVenueDialogQrCodeTests()
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

    /// <summary>
    /// Unlike the marquee's, these are not behind a switch: a venue cannot turn codes on, only say
    /// where they land when something else shows one.
    /// </summary>
    [Fact]
    public void TheSettings_AreShownWithoutAnythingBeingTurnedOn()
    {
        var cut = Render(new Venue.VenueSettings());

        Assert.Single(cut.FindAll(CornerSelector));
        Assert.Single(cut.FindAll(SizeSelector));
        Assert.Single(cut.FindAll(HideSelector));
    }

    /// <summary>
    /// The venue stores null for "no preference", which a select cannot show. It offers the corner
    /// and size a code would take anyway, so saving without touching them changes nothing.
    /// </summary>
    [Fact]
    public void AVenueThatHasNeverBeenAsked_ShowsWhatACodeWouldTakeAnyway()
    {
        var cut = Render(new Venue.VenueSettings());

        Assert.Equal(nameof(ScreenCorner.BottomRight), cut.Find(CornerSelector).GetAttribute("value"));
        Assert.Equal(nameof(ScreenQrSize.Medium), cut.Find(SizeSelector).GetAttribute("value"));
        Assert.False(cut.Find(HideSelector).HasAttribute("checked"));
    }

    [Fact]
    public void AVenuesOwnChoices_AreWhatItSeesAgain()
    {
        var cut = Render(new Venue.VenueSettings
        {
            QrCodeCorner = ScreenCorner.TopLeft,
            QrCodeSize = ScreenQrSize.Large,
            QrCodeHideDuringSong = true,
        });

        Assert.Equal(nameof(ScreenCorner.TopLeft), cut.Find(CornerSelector).GetAttribute("value"));
        Assert.Equal(nameof(ScreenQrSize.Large), cut.Find(SizeSelector).GetAttribute("value"));
        Assert.True(cut.Find(HideSelector).HasAttribute("checked"));
    }

    /// <summary>The whole point: what the host picks here has to reach the venue that is saved.</summary>
    [Fact]
    public void WhatTheHostPicks_ReachesTheSavedVenue()
    {
        Venue? saved = null;
        var cut = Render(new Venue.VenueSettings(), venue => saved = venue);

        cut.Find(CornerSelector).Change(nameof(ScreenCorner.TopRight));
        cut.Find(SizeSelector).Change(nameof(ScreenQrSize.Small));
        cut.Find(HideSelector).Change(true);
        cut.Find("form").Submit();

        Assert.NotNull(saved);
        Assert.Equal(ScreenCorner.TopRight, saved!.Settings.QrCodeCorner);
        Assert.Equal(ScreenQrSize.Small, saved.Settings.QrCodeSize);
        Assert.True(saved.Settings.QrCodeHideDuringSong);
    }

    /// <summary>
    /// Saving a venue that never chose has to write the values it was shown, or the dialog would
    /// have described a placement the screen then ignored.
    /// </summary>
    [Fact]
    public void SavingWithoutTouchingThem_WritesWhatWasOffered()
    {
        Venue? saved = null;
        var cut = Render(new Venue.VenueSettings(), venue => saved = venue);

        cut.Find("form").Submit();

        Assert.NotNull(saved);
        Assert.Equal(ScreenCorner.BottomRight, saved!.Settings.QrCodeCorner);
        Assert.Equal(ScreenQrSize.Medium, saved.Settings.QrCodeSize);
    }

    private IRenderedComponent<EditVenueDialog> Render(
        Venue.VenueSettings settings, Action<Venue>? onSave = null)
        => Render<EditVenueDialog>(ps => ps
            .Add(p => p.IsOpen, true)
            .Add(p => p.Venue, new Venue { Name = "Test Venue", Settings = settings })
            .Add(p => p.OnSave, venue => onSave?.Invoke(venue)));
}
