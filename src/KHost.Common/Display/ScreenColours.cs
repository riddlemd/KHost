using KHost.Abstractions.Models;

namespace KHost.Common.Display;

/// <summary>Every colour a venue asks a display to draw in, each as <c>#rrggbb</c> or null for the
/// display's own.</summary>
/// <remarks>A venue's own setting wins; one left unset takes the theme colour it falls back to; a
/// colour with neither is null. <see cref="VisualisationPalette"/> is main, light and dark, for a
/// visualisation that respects the venue's theme.</remarks>
public sealed record ScreenColourSet
{
    /// <inheritdoc cref="Venue.VenueSettings.ScreenBackgroundColor"/>
    public string? Background { get; init; }
    /// <inheritdoc cref="Venue.VenueSettings.LyricsSungColor"/>
    public string? LyricsSung { get; init; }
    /// <inheritdoc cref="Venue.VenueSettings.LyricsUnsungColor"/>
    public string? LyricsUnsung { get; init; }
    /// <inheritdoc cref="Venue.VenueSettings.LyricsOutlineColor"/>
    public string? LyricsOutline { get; init; }
    /// <inheritdoc cref="Venue.VenueSettings.IntroTextColor"/>
    public string? IntroText { get; init; }
    /// <inheritdoc cref="Venue.VenueSettings.IntroOutlineColor"/>
    public string? IntroOutline { get; init; }
    /// <inheritdoc cref="Venue.VenueSettings.NextSingerTextColor"/>
    public string? NextSingerText { get; init; }
    /// <inheritdoc cref="Venue.VenueSettings.NextSingerNameColor"/>
    public string? NextSingerName { get; init; }
    /// <inheritdoc cref="Venue.VenueSettings.NextSingerPanelColor"/>
    public string? NextSingerPanel { get; init; }
    /// <inheritdoc cref="Venue.VenueSettings.BreakMusicCardTextColor"/>
    public string? BreakMusicCardText { get; init; }
    /// <inheritdoc cref="Venue.VenueSettings.BreakMusicCardBackgroundColor"/>
    public string? BreakMusicCardBackground { get; init; }
    /// <inheritdoc cref="Venue.VenueSettings.QrCodeFrameColor"/>
    public string? QrCodeFrame { get; init; }
    /// <inheritdoc cref="Venue.VenueSettings.QrCodeCaptionColor"/>
    public string? QrCodeCaption { get; init; }
    /// <inheritdoc cref="Venue.VenueSettings.MarqueeBackgroundColor"/>
    public string? MarqueeBackground { get; init; }
    /// <inheritdoc cref="Venue.VenueSettings.MarqueeTextColor"/>
    public string? MarqueeText { get; init; }
    /// <inheritdoc cref="Venue.VenueSettings.MarqueeSingerColor"/>
    public string? MarqueeSinger { get; init; }
    /// <inheritdoc cref="Venue.VenueSettings.MarqueeSongColor"/>
    public string? MarqueeSong { get; init; }
    /// <inheritdoc cref="Venue.VenueSettings.MarqueeDividerColor"/>
    public string? MarqueeDivider { get; init; }

    /// <summary>Main, light and dark, or null at a venue whose theme names none of them.</summary>
    public IReadOnlyList<string>? VisualisationPalette { get; init; }
}

/// <summary>Resolves a venue's colours for a display.</summary>
public static class ScreenColours
{
    /// <summary>The colours the venue's settings describe.</summary>
    public static ScreenColourSet ResolveScreenColours(this Venue.VenueSettings settings)
    {
        var primary = HexOrNull(settings.ThemePrimaryColor);
        var highlight = HexOrNull(settings.ThemeHighlightColor);
        var text = HexOrNull(settings.ThemeTextColor);
        var shadow = HexOrNull(settings.ThemeShadowColor);

        string? Own(string? setting, string? theme) => HexOrNull(setting) ?? theme;

        var main = primary ?? highlight ?? shadow;

        return new ScreenColourSet
        {
            Background = Own(settings.ScreenBackgroundColor, shadow),
            LyricsSung = Own(settings.LyricsSungColor, highlight),
            LyricsUnsung = Own(settings.LyricsUnsungColor, text),
            LyricsOutline = Own(settings.LyricsOutlineColor, shadow),
            IntroText = Own(settings.IntroTextColor, text),
            IntroOutline = Own(settings.IntroOutlineColor, shadow),
            NextSingerText = Own(settings.NextSingerTextColor, text),
            NextSingerName = Own(settings.NextSingerNameColor, highlight),
            NextSingerPanel = Own(settings.NextSingerPanelColor, shadow),
            BreakMusicCardText = Own(settings.BreakMusicCardTextColor, text),
            BreakMusicCardBackground = Own(settings.BreakMusicCardBackgroundColor, shadow),
            QrCodeFrame = Own(settings.QrCodeFrameColor, text),
            QrCodeCaption = Own(settings.QrCodeCaptionColor, shadow),
            MarqueeBackground = Own(settings.MarqueeBackgroundColor, shadow),
            MarqueeText = Own(settings.MarqueeTextColor, text),
            MarqueeSinger = Own(settings.MarqueeSingerColor, primary),
            MarqueeSong = Own(settings.MarqueeSongColor, highlight),
            MarqueeDivider = Own(settings.MarqueeDividerColor, primary),
            VisualisationPalette = main is null ? null : [main, highlight ?? main, shadow ?? main],
        };
    }

    /// <summary>Whether <paramref name="colour"/> is <c>#rrggbb</c>, the one form a colour is kept in.</summary>
    public static bool IsHexColour(string? colour)
        => colour is { } c && c.Trim() is { Length: 7 } t && t[0] == '#' && t.Skip(1).All(Uri.IsHexDigit);

    /// <summary><paramref name="colour"/> trimmed and lowercased when it is <c>#rrggbb</c>; null otherwise.</summary>
    public static string? HexOrNull(string? colour) => IsHexColour(colour) ? colour!.Trim().ToLowerInvariant() : null;
}
