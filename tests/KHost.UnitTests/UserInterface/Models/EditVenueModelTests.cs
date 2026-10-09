using System.Reflection;
using KHost.Abstractions.Models;
using KHost.Abstractions.Models.QueueRotation;
using KHost.UserInterface.Models;

namespace KHost.UnitTests.UserInterface.Models;

public class EditVenueModelTests
{
    /// <summary>Reflects over every VenueSettings property rather than naming them, so a setting
    /// added to the model and left out of <see cref="EditVenueModel.From"/> or
    /// <see cref="EditVenueModel.ApplyTo"/> fails this test instead of quietly not saving. Every
    /// value below is deliberately non-default and non-boundary, so none of the model's own
    /// null/zero fallbacks or clamps kick in and mask a missing mapping.</summary>
    [Fact]
    public void FromThenApplyTo_RoundTripsEveryVenueSetting()
    {
        var source = new Venue { Name = "Round Trip Room", Settings = DistinctSettings() };
        var target = new Venue { Name = "Target Room" };

        var model = EditVenueModel.From(source, new NewVenueDefaults(Guid.NewGuid(), Guid.NewGuid(), ImageScaling.Stretch));
        model.ApplyTo(target);

        foreach (var property in typeof(Venue.VenueSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            // QueueRotation is cloned, so it gets its own check below rather than a reference
            // comparison here.
            if (property.Name is nameof(Venue.VenueSettings.QueueRotation))
                continue;

            var expected = property.GetValue(source.Settings);
            var actual = property.GetValue(target.Settings);

            Assert.True(Equals(expected, actual),
                $"{property.Name}: expected {expected}, got {actual} — check EditVenueModel.From/ApplyTo");
        }

        Assert.Equal(source.Settings.QueueRotation!.StrategyId, target.Settings.QueueRotation!.StrategyId);
    }

    /// <summary>A brand-new venue — the dialog's Add path — starts on the playlist and placeholder
    /// image it is handed rather than black and blank.</summary>
    [Fact]
    public void From_ANewVenue_StartsOnTheDefaultsItIsHanded()
    {
        var defaults = new NewVenueDefaults(Guid.NewGuid(), Guid.NewGuid(), ImageScaling.Fill);

        var model = EditVenueModel.From(null, defaults);

        Assert.Equal(
            (defaults.VisualisationPlaylistId, defaults.PlaceholderImageMediaId, defaults.PlaceholderImageScaling),
            (model.VisualisationPlaylistId, model.BrandingImageMediaId, model.BrandingImageScaling));
    }

    /// <summary>An existing venue's own choices, "none" included, are never overridden.</summary>
    [Fact]
    public void From_AnExistingVenueWithNoPlaylistOrImage_StaysOnNone()
    {
        var model = EditVenueModel.From(new Venue { Name = "Dark Room" }, new NewVenueDefaults(Guid.NewGuid(), Guid.NewGuid(), ImageScaling.Fill));

        Assert.Equal((null, null, null), (model.VisualisationPlaylistId, model.BrandingImageMediaId, model.BrandingImageScaling));
    }

    /// <summary>Every VenueSettings field, given a value nothing in From/ApplyTo treats specially:
    /// no nulls (some become screen-default fallbacks), no zeros where zero means "unset", and
    /// values already inside every Math.Clamp range.</summary>
    private static Venue.VenueSettings DistinctSettings() => new()
    {
        ShowEstimatedWaitTime = false,
        TippingEnabled = false,
        WarnOnDuplicateSong = true,
        DuplicateSongWindowHours = 8,
        RefuseSongQueuedForAnotherSinger = true,
        RemoteSongLimit = 3,
        PromptBeforeRemovingSinger = false,
        PromptBeforeRemovingPerformance = false,
        ClearQueueOnClose = false,
        QueueRotation = new QueueRotationConfig { StrategyId = "weighted-fair" },
        BrandingImageMediaId = Guid.NewGuid(),
        AdPoolId = Guid.NewGuid(),
        BreakMusicPoolId = Guid.NewGuid(),
        AllowAliases = true,
        AllowGuestRemote = false,
        ShowQueueToGuests = false,
        MarqueeEnabled = true,
        MarqueeSingerCount = 7,
        MarqueeMessage = "Two-for-one Tuesdays",
        MarqueeEntryFormat = "{song}",
        MarqueePosition = MarqueePosition.Top,
        MarqueeBackgroundColor = "#123456",
        MarqueeTextColor = "#abcdef",
        MarqueeFontSizePixels = 40,
        MarqueeScrollSpeed = 120,
        MarqueePinLabel = true,
        MarqueeBackgroundOpacity = 55,
        MarqueeSingerColor = "#111111",
        MarqueeSongColor = "#222222",
        MarqueeDividerColor = "#333333",
        ThemePrimaryColor = "#b00001",
        ThemeHighlightColor = "#b00002",
        ThemeTextColor = "#b00003",
        ThemeShadowColor = "#b00004",
        ScreenBackgroundColor = "#b00005",
        LyricsSungColor = "#b00006",
        LyricsUnsungColor = "#b00007",
        LyricsOutlineColor = "#b00008",
        IntroTextColor = "#b00009",
        IntroOutlineColor = "#b0000a",
        NextSingerTextColor = "#b0000b",
        NextSingerNameColor = "#b0000c",
        NextSingerPanelColor = "#b0000d",
        BreakMusicCardTextColor = "#b0000e",
        BreakMusicCardBackgroundColor = "#b0000f",
        QrCodeFrameColor = "#b00010",
        QrCodeCaptionColor = "#b00011",
        MarqueeDividerShape = MarqueeDividerShape.Diamond,
        MarqueeHideDuringSong = true,
        VisualisationPlaylistId = Guid.NewGuid(),
        NextSingerBackground = NextSingerBackground.Visualisation,
        QrCodeSource = "khost.plugins.example",
        QrCodeCorner = OverlayCorner.TopLeft,
        QrCodeSize = QrCodeSize.Large,
        QrCodeHideDuringSong = true,
        QrCodeSafeZone = 3,
        QrCodeOffset = 5.5,
        BrandingImageScaling = ImageScaling.Fill,
        BreakMusicCardEnabled = true,
        BreakMusicCardCorner = OverlayCorner.TopRight,
    };
}
