using KHost.Abstractions.Models;

namespace KHost.Common.Display;

/// <summary>The words a venue's marquee says, composed the way the venue's settings describe them.</summary>
/// <remarks>For a display that draws a marquee: the venue's settings say what the band carries, and
/// <see cref="KHost.Abstractions.Services.IUpNextService"/> says who. How it looks and when it is
/// drawn stay the display's.</remarks>
public static class MarqueeText
{
    /// <summary>The entry a venue gets when it has written no wording of its own.</summary>
    public const string DefaultEntryFormat = "{song} - {singer}";

    /// <summary>One upcoming turn as the band reads it, from the venue's
    /// <see cref="Venue.VenueSettings.MarqueeEntryFormat"/>.</summary>
    /// <param name="entry">The turn to name.</param>
    /// <param name="entryFormat">The venue's wording; null or blank uses
    /// <see cref="DefaultEntryFormat"/>.</param>
    /// <returns>The format with <c>{song}</c>, <c>{artist}</c>, <c>{singer}</c> and
    /// <c>{position}</c> replaced, matched without regard to case; a tag the format leaves out is
    /// simply not shown. A singer with nothing queued is named alone, whatever the format.</returns>
    public static string ComposeEntry(UpNextEntry entry, string? entryFormat)
    {
        if (entry.Title is null)
            return entry.Singer;

        var format = string.IsNullOrWhiteSpace(entryFormat) ? DefaultEntryFormat : entryFormat;

        return format
            .Replace("{song}", entry.Title, StringComparison.OrdinalIgnoreCase)
            .Replace("{artist}", entry.Artist ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{singer}", entry.Singer, StringComparison.OrdinalIgnoreCase)
            .Replace("{position}", entry.Position.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The venue's <see cref="Venue.VenueSettings.MarqueeMessage"/> as one line.</summary>
    /// <returns>Every run of whitespace, line breaks included, as one space, trimmed; null for a
    /// null or blank message, which means the band carries no message.</returns>
    public static string? CollapseToOneLine(string? message)
        => string.IsNullOrWhiteSpace(message)
            ? null
            : string.Join(' ', message.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
