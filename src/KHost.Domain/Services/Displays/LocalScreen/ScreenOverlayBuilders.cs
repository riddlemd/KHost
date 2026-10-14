using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Common.Display;
using KHost.IPC.SignalR.Contracts;
using QRCoder;

namespace KHost.Domain.Services.Displays.LocalScreen;

/// <summary>The local screen's overlays as the screen draws them, built whole from the venue's
/// settings and what is current. Decides the look; sends nothing.</summary>
internal static class ScreenOverlayBuilders
{
    /// <summary>One module of white: the standard four-module border reads as a slab over video.</summary>
    private const int DefaultSafeZone = 1;

    /// <summary>Almost flush, as a fraction of the shorter side: overscan crops a flush edge. Shared by
    /// everything in a corner, since the inset belongs to the corner and not to what sits in it.</summary>
    private const double DefaultOffset = 0.2;

    /// <summary>Bottom-left, away from the code's default corner, so the two stack when unset.</summary>
    private const OverlayCorner DefaultBreakMusicCardCorner = OverlayCorner.BottomLeft;

    /// <summary>The marquee as the screen draws it, whole, from the venue's settings and who is next.</summary>
    /// <remarks>Disabled with no venue selected, one that has the marquee off, or one that hides it
    /// while <paramref name="performanceUnderWay"/>. The singers are
    /// exactly what <see cref="IUpNextService"/> answers for the venue's count, so the band and
    /// anything else naming who is next cannot disagree.</remarks>
    internal static async Task<SetMarqueeCommand> BuildMarqueeAsync(
        Venue.VenueSettings? settings, IUpNextService upNext, bool performanceUnderWay = false)
    {
        if (settings is null || !settings.MarqueeEnabled)
            return new SetMarqueeCommand { Enabled = false };

        if (settings.MarqueeHideDuringSong && performanceUnderWay)
            return new SetMarqueeCommand { Enabled = false };

        var upcoming = await upNext.ReadAsync(settings.MarqueeSingerCount);
        var glyph = MarqueeEntrySegmenter.ResolveGlyph(settings.MarqueeDividerShape);
        var colours = settings.ResolveScreenColours();

        var segments = new List<MarqueeSegment>();
        foreach (var (entry, index) in upcoming.Select((entry, index) => (entry, index)))
        {
            // Glyph null means "None" was chosen: entries run together with no divider at all.
            if (index > 0 && glyph is not null)
                segments.Add(new MarqueeSegment { Text = "", Kind = MarqueeSegmentKind.Separator });

            segments.AddRange(MarqueeEntrySegmenter.ComposeSegments(entry, settings.MarqueeEntryFormat));
        }

        return new SetMarqueeCommand
        {
            Enabled = true,
            Entries = segments,
            Message = MarqueeText.CollapseToOneLine(settings.MarqueeMessage),
            Position = settings.MarqueePosition,

            BackgroundColor = colours.MarqueeBackground,
            TextColor = colours.MarqueeText,
            SingerColor = colours.MarqueeSinger,
            SongColor = colours.MarqueeSong,
            DividerColor = colours.MarqueeDivider,
            DividerGlyph = glyph,
            BackgroundOpacityPercent = settings.MarqueeBackgroundOpacity,
            FontSizePixels = settings.MarqueeFontSizePixels,
            ScrollSpeed = settings.MarqueeScrollSpeed,
            PinLabel = settings.MarqueePinLabel,
        };
    }

    /// <summary>The venue's colours for everything drawn besides the marquee; every one null, the
    /// screen's own, with no venue selected.</summary>
    internal static SetScreenThemeCommand BuildScreenTheme(Venue.VenueSettings? settings)
    {
        if (settings is null) return new SetScreenThemeCommand();

        var colours = settings.ResolveScreenColours();
        return new SetScreenThemeCommand
        {
            Background = colours.Background,
            LyricsSung = colours.LyricsSung,
            LyricsUnsung = colours.LyricsUnsung,
            LyricsOutline = colours.LyricsOutline,
            IntroText = colours.IntroText,
            IntroOutline = colours.IntroOutline,
            NextSingerText = colours.NextSingerText,
            NextSingerName = colours.NextSingerName,
            NextSingerPanel = colours.NextSingerPanel,
            BreakMusicCardText = colours.BreakMusicCardText,
            BreakMusicCardBackground = colours.BreakMusicCardBackground,
            QrCodeFrame = colours.QrCodeFrame,
            QrCodeCaption = colours.QrCodeCaption,
        };
    }

    /// <summary>The offer as the screen draws it, or an empty set that clears whatever is up.</summary>
    internal static SetScreenQrCodesCommand BuildQrCodes(QrCodeOffer? offer, Func<string, (string Image, int Modules)> encode)
    {
        if (offer is null)
            return new SetScreenQrCodesCommand();

        var (image, modules) = encode(offer.Payload);

        return new SetScreenQrCodesCommand
        {
            Codes =
            [
                new ScreenQrCodePlacement
                {
                    ImageUrl = image,
                    Modules = modules,
                    Caption = offer.Caption,
                    Corner = offer.Corner ?? OverlayCorner.BottomRight,
                    Size = offer.Size ?? QrCodeSize.Medium,

                    // Resolved here, not on the screen, which decides nothing.
                    SafeZone = offer.SafeZone ?? DefaultSafeZone,
                    Offset = offer.Offset ?? DefaultOffset,
                },
            ],
        };
    }

    /// <summary>Encodes the payload as an SVG at the lowest correction level, fewer modules.</summary>
    internal static (string Image, int Modules) EncodeQrCode(string payload)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.L);

        // One unit per module, so every size the screen asks for is an exact multiple of the grid.
        // No quiet zone drawn in: the screen paints that margin, so a corner can be as tight as it likes.
        var svg = new SvgQRCode(data).GetGraphic(1, "#000000", "#ffffff", drawQuietZones: false);

        // ModuleMatrix counts the quiet zone whether or not it is drawn, so the eight rows and
        // columns of it come off: what the screen sizes against has to be what is in the picture.
        return (
            $"data:image/svg+xml;base64,{Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(svg))}",
            data.ModuleMatrix.Count - 8);
    }

    /// <summary>Names the break music in a corner; says what is playing, not what is cued.</summary>
    internal static SetBreakMusicCardCommand BuildBreakMusicCard(Venue.VenueSettings? settings, IBreakMusicService breakMusic)
    {
        // A venue that wants none gets none, and a console with no venue selected has nobody to
        // have asked, which is the same answer.
        if (settings is null || !settings.BreakMusicCardEnabled)
            return new SetBreakMusicCardCommand { Enabled = false };

        // Playing only: Paused and Suspended both mean the room is hearing something else, and a
        // card naming a track nobody can hear is worse than no card.
        if (breakMusic.State != BreakMusicState.Playing || breakMusic.CurrentTrack is not { } track)
            return new SetBreakMusicCardCommand { Enabled = false };

        // A provider that reports no title has nothing worth a corner of the picture.
        if (string.IsNullOrWhiteSpace(track.Title))
            return new SetBreakMusicCardCommand { Enabled = false };

        return new SetBreakMusicCardCommand
        {
            Enabled = true,
            Title = track.Title,

            // Null rather than blank where the provider could not say, so the screen draws one
            // line instead of a gap it has to reason about.
            Artist = string.IsNullOrWhiteSpace(track.Artist) ? null : track.Artist,
            Corner = settings.BreakMusicCardCorner ?? DefaultBreakMusicCardCorner,

            // The codes' setting, because the inset belongs to the corner.
            Offset = settings.QrCodeOffset > 0 ? settings.QrCodeOffset : DefaultOffset,
        };
    }
}
