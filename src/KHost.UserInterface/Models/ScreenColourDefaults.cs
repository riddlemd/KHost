namespace KHost.UserInterface.Models;

/// <summary>The local screen's own colours, which a venue with no colour and no theme colour for a
/// part sees. The dialog shows them so a colour picker never opens on a colour nobody chose.</summary>
/// <remarks>The screen's page holds the same values as its CSS and lyric fallbacks.</remarks>
public static class ScreenColourDefaults
{
    public const string Background = "#000000";
    public const string LyricsSung = "#8558fa";
    public const string LyricsUnsung = "#ffffff";
    public const string LyricsOutline = "#000000";
    public const string IntroText = "#ffffff";
    public const string IntroOutline = "#000000";
    public const string NextSingerText = "#ffffff";
    public const string NextSingerName = "#ffffff";
    public const string NextSingerPanel = "#000000";
    public const string BreakMusicCardText = "#ffffff";
    public const string BreakMusicCardBackground = "#101014";
    public const string QrCodeFrame = "#ffffff";
    public const string QrCodeCaption = "#101014";
    public const string MarqueeBackground = "#000000";
    public const string MarqueeText = "#f2f2f5";
    public const string MarqueeSinger = ThemePrimary;
    public const string MarqueeSong = ThemeHighlight;

    /// <summary>What a theme colour starts on when a host first sets it.</summary>
    public const string ThemePrimary = "#8558fa";
    public const string ThemeHighlight = "#33ccff";
    public const string ThemeText = "#ffffff";
    public const string ThemeShadow = "#000000";
}
