using System.Globalization;
using KHost.Abstractions.Models;

namespace KHost.Domain.Services;

/// <summary>Gives the words a song's timing leaves uncoloured the venue's colours.</summary>
/// <remarks>Applied to the timing itself, ahead of every other adjustment, so the screen, the
/// burn-in and the colour-blind adjustment all see the venue's colour rather than each falling
/// back to its own. A colour the timing names is kept.</remarks>
public static class VenueLyricColours
{
    /// <summary>The lyrics with every unset sung, unsung and count-in edge colour filled in, or
    /// <paramref name="lyrics"/> itself when nothing was unset that the venue colours.</summary>
    public static TimedLyrics FillUnset(TimedLyrics lyrics, LyricColor? sung, LyricColor? unsung, LyricColor? outline)
    {
        var changed = false;

        var pages = lyrics.Pages.Select(page =>
        {
            if ((page.Active is not null || sung is null) && (page.Inactive is not null || unsung is null)) return page;
            changed = true;
            return page with { Active = page.Active ?? sung, Inactive = page.Inactive ?? unsung };
        }).ToList();

        var countIns = lyrics.CountIns.Select(countIn =>
        {
            if ((countIn.Active is not null || sung is null) && (countIn.Inactive is not null || unsung is null)
                && (countIn.Border is not null || outline is null)) return countIn;
            changed = true;
            return countIn with { Active = countIn.Active ?? sung, Inactive = countIn.Inactive ?? unsung, Border = countIn.Border ?? outline };
        }).ToList();

        return changed ? lyrics with { Pages = pages, CountIns = countIns } : lyrics;
    }

    /// <summary><c>#rrggbb</c> as a lyric colour; null for null or anything else.</summary>
    public static LyricColor? FromHex(string? hex)
    {
        if (hex is not { Length: 7 } || hex[0] != '#'
            || !int.TryParse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            return null;

        return new LyricColor((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    }
}
