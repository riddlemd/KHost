using SkiaSharp;

namespace KHost.Domain.Services.BurnIn;

/// <summary>The typefaces burned-in words are painted with.</summary>
/// <remarks>The screen draws in its web view's <c>sans-serif</c>, which is Helvetica on macOS, Arial on
/// Windows and usually DejaVu Sans on Linux — so the stack asks for those, in that order, and the
/// burned-in words land at the widths the screen's do. Nothing is bundled: when none of them is
/// installed, Skia's own default face is used, and a character that face lacks is looked up per
/// line through the system's fallback. A machine with no fonts at all paints no words, and says so
/// only by their absence.</remarks>
internal static class LyricFonts
{
    /// <summary>The screen's words are drawn at weight 600.</summary>
    public const int WordsWeight = 600;

    /// <summary>The countdown over a count-in is drawn at weight 800.</summary>
    public const int CountdownWeight = 800;

    private static readonly string[] SansSerif = ["Helvetica", "Arial", "DejaVu Sans", "Liberation Sans", "Noto Sans"];

    private static readonly Lazy<SKTypeface> _words = new(() => Resolve(WordsWeight));
    private static readonly Lazy<SKTypeface> _countdown = new(() => Resolve(CountdownWeight));

    public static SKTypeface Words => _words.Value;

    public static SKTypeface Countdown => _countdown.Value;

    /// <summary>The face a line is shaped in: the words face, or a system fallback for the first
    /// character it cannot draw.</summary>
    /// <remarks>One face per line, because the line is shaped as one run. A line mixing two scripts
    /// neither face covers draws the second as missing glyphs.</remarks>
    public static SKTypeface For(string text)
    {
        var primary = Words;

        foreach (var rune in text.EnumerateRunes())
        {
            if (rune.Value <= 0x7F || primary.ContainsGlyph(rune.Value)) continue;

            return SKFontManager.Default.MatchCharacter(
                primary.FamilyName, new SKFontStyle(WordsWeight, (int)SKFontStyleWidth.Normal, SKFontStyleSlant.Upright),
                null, rune.Value) ?? primary;
        }

        return primary;
    }

    private static SKTypeface Resolve(int weight)
    {
        var style = new SKFontStyle(weight, (int)SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);

        foreach (var family in SansSerif)
        {
            if (SKFontManager.Default.MatchFamily(family, style) is { } typeface) return typeface;
        }

        return SKTypeface.FromFamilyName(null, style) ?? SKTypeface.Default;
    }
}
