using KHost.Abstractions.Models;

namespace KHost.Domain.Services;

/// <summary>Gives a lead-in to the lines a singer has to find their way back into — a page's first
/// line, a line after a long silence, and a word a long silence falls before part way along a line —
/// where the timing supplied none.</summary>
/// <remarks>Applied to the timing itself, so the screen and the burn-in draw the same markers from
/// the same data. A lead-in the timing did supply is never touched.</remarks>
public static class LeadInGenerator
{
    /// <summary>The silence before a line that earns it a lead-in when no setting says otherwise.</summary>
    public const int DefaultLongPauseSeconds = 3;

    /// <summary>The silence a page's opener needs before it earns one; shorter than for any other
    /// line, since a new page is itself something to find the way into.</summary>
    /// <remarks>Without it, timing whose pages turn a line at a time puts a marker on nearly every
    /// line, running while the singer is still on the one before.</remarks>
    public const double OpenerPauseSeconds = 1.0;

    /// <summary>The longest a marker travels.</summary>
    public const double MaxRunSeconds = 2.0;

    /// <summary>A marker with less time than this to travel is a flicker, not a cue, and is dropped.</summary>
    public const double MinRunSeconds = 0.5;

    /// <summary>How far outside the line the marker sets off, as a share of the timing's width.</summary>
    /// <remarks>A twentieth matches the run providers' own lead-ins make, so the two look alike.</remarks>
    public const double RunWidthFraction = 0.05;

    /// <param name="longPauseSeconds">The silence before a line, page opener or not, or before a word
    /// inside one, that earns it one.</param>
    public static TimedLyrics AddMissing(TimedLyrics lyrics, double longPauseSeconds)
    {
        var sung = lyrics.Pages
            .SelectMany(page => page.Lines)
            .SelectMany(line => line.Syllables)
            .OrderBy(syllable => syllable.StartSeconds)
            .ToArray();
        var starts = sung.Select(syllable => syllable.StartSeconds).ToArray();

        // Prefix max, not the previous syllable's end: in a duet an earlier syllable can outlast a later one.
        var endedBy = new double[sung.Length];
        for (var i = 0; i < sung.Length; i++)
            endedBy[i] = Math.Max(i > 0 ? endedBy[i - 1] : double.NegativeInfinity, sung[i].EndSeconds);

        var changed = false;
        var pages = new List<LyricPage>(lyrics.Pages.Count);

        foreach (var page in lyrics.Pages)
        {
            // The line sung first, which is not always the top one.
            var opener = page.Lines
                .Where(line => line.Syllables.Count > 0)
                .MinBy(line => line.Syllables[0].StartSeconds);

            var lines = new List<LyricLine>(page.Lines.Count);
            foreach (var line in page.Lines)
            {
                var leadIn = line.LeadIn ?? Generate(lyrics, page, line, ReferenceEquals(line, opener), longPauseSeconds, starts, endedBy);
                changed |= leadIn != line.LeadIn;
                lines.Add(leadIn == line.LeadIn ? line : line with { LeadIn = leadIn });
            }

            pages.Add(page with { Lines = lines });
        }

        return changed ? lyrics with { Pages = pages } : lyrics;
    }

    private static LyricLeadIn? Generate(
        TimedLyrics lyrics, LyricPage page, LyricLine line, bool opensPage, double longPauseSeconds, double[] starts, double[] endedBy)
    {
        if (line.Position is not { } box || line.Syllables.Count == 0) return null;

        // Held inside the frame on the leading side; the drawers mirror the same run for right to left.
        var room = lyrics.IsRightToLeft
            ? lyrics.Bounds.X + lyrics.Bounds.Width - (box.X + box.Width)
            : box.X - lyrics.Bounds.X;
        var run = Math.Clamp(lyrics.Bounds.Width * RunWidthFraction, 0, Math.Max(room, 0));
        if (run <= 0) return null;

        var first = line.Syllables[0].StartSeconds;
        var pause = first - PreviousEnd(first, starts, endedBy);
        if (pause >= (opensPage ? Math.Min(OpenerPauseSeconds, longPauseSeconds) : longPauseSeconds))
        {
            // An opener runs from the page's arrival, so it never sets off before the page is up.
            var start = first - Math.Min(MaxRunSeconds, opensPage ? first - page.ShowFromSeconds : pause);
            if (first - start >= MinRunSeconds) return new LyricLeadIn(start, box.X - run);
        }

        // A line carries one lead-in, so only the first pause inside it that earns one gets it.
        for (var i = 1; i < line.Syllables.Count; i++)
        {
            var at = line.Syllables[i].StartSeconds;
            var gap = at - PreviousEnd(at, starts, endedBy);
            if (gap < longPauseSeconds) continue;

            var travel = Math.Min(MaxRunSeconds, gap);
            if (travel >= MinRunSeconds) return new LyricLeadIn(at - travel, box.X - run) { ArriveAtSyllable = i };
        }

        return null;
    }

    /// <summary>When the singing before <paramref name="at"/> last stopped, or minus infinity if none came before.</summary>
    private static double PreviousEnd(double at, double[] starts, double[] endedBy)
    {
        var index = Array.BinarySearch(starts, at);
        if (index < 0) index = ~index;
        else while (index > 0 && starts[index - 1] >= at) index--;

        return index == 0 ? double.NegativeInfinity : endedBy[index - 1];
    }
}
