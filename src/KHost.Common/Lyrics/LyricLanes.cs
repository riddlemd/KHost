using KHost.Abstractions.Models;

namespace KHost.Common.Lyrics;

/// <summary>When each singer of a song is singing, read from its <see cref="TimedLyrics"/>: one lane
/// per singer, each a run of sung stretches, for a timeline that shows who sings where.</summary>
public static class LyricLanes
{
    /// <summary>The gap used when a caller names none.</summary>
    /// <remarks>Longer than a breath between two lines of one verse, shorter than an instrumental
    /// break, so a lane reads as whole sections rather than as one bar per line.</remarks>
    public static readonly TimeSpan DefaultMergeGap = TimeSpan.FromSeconds(4);

    /// <summary>One lane per singer, in the order the singers are first heard.</summary>
    /// <param name="lyrics">The timing to read.</param>
    /// <param name="mergeGap">Two stretches of one singer closer than this become one; null uses
    /// <see cref="DefaultMergeGap"/>.</param>
    /// <returns>A lane for every <see cref="LyricPage.Voice"/> that sings anything, pages with no
    /// voice forming one lane of their own. A stretch runs from a line's first syllable to its
    /// last; stretches within a lane are in time order, each starting at least the gap after the
    /// one before it ends. A lane's colour is the <see cref="LyricPage.Active"/> most of its pages
    /// carry, or null when none carries one. Empty when nothing is sung.</returns>
    public static IReadOnlyList<LyricLane> SungSpansByVoice(TimedLyrics lyrics, TimeSpan? mergeGap = null)
    {
        var gap = (mergeGap ?? DefaultMergeGap).TotalSeconds;
        var singers = new List<Singer>();

        foreach (var page in lyrics.Pages)
        {
            var spans = LinesOf(page).ToList();
            if (spans.Count == 0) continue;

            var singer = singers.Find(s => s.Voice == page.Voice);
            if (singer is null)
                singers.Add(singer = new Singer(page.Voice));

            singer.Spans.AddRange(spans);
            if (page.Active is not null) singer.Colors.Add(page.Active);
        }

        return
        [
            .. singers
                .Select(s => new LyricLane(s.Voice, MostCommon(s.Colors), Merge(s.Spans, gap)))
                .OrderBy(lane => lane.Spans[0].StartSeconds),
        ];
    }

    /// <summary>Every sung stretch of every singer as one lane, for a timeline with no room to
    /// tell singers apart.</summary>
    /// <param name="lyrics">The timing to read.</param>
    /// <param name="mergeGap">As for <see cref="SungSpansByVoice"/>.</param>
    /// <returns>A lane with no voice and no colour, since it speaks for every singer; null when
    /// nothing is sung.</returns>
    public static LyricLane? SungSpansAsOneLane(TimedLyrics lyrics, TimeSpan? mergeGap = null)
    {
        var spans = lyrics.Pages.SelectMany(LinesOf).ToList();

        return spans.Count == 0
            ? null
            : new LyricLane(null, null, Merge(spans, (mergeGap ?? DefaultMergeGap).TotalSeconds));
    }

    private static IEnumerable<SungSpan> LinesOf(LyricPage page)
        => page.Lines
            .Where(line => line.Syllables.Count > 0)
            .Select(line => new SungSpan(
                line.Syllables[0].StartSeconds,
                Math.Max(line.Syllables[0].StartSeconds, line.Syllables[^1].EndSeconds)));

    private static IReadOnlyList<SungSpan> Merge(List<SungSpan> spans, double gapSeconds)
    {
        var merged = new List<SungSpan>();

        foreach (var span in spans.OrderBy(s => s.StartSeconds))
        {
            if (merged.Count > 0 && span.StartSeconds - merged[^1].EndSeconds < gapSeconds)
                merged[^1] = merged[^1] with { EndSeconds = Math.Max(merged[^1].EndSeconds, span.EndSeconds) };
            else
                merged.Add(span);
        }

        return merged;
    }

    // Ties go to the colour seen first, so a lane's colour does not depend on dictionary order.
    private static LyricColor? MostCommon(List<LyricColor> colors)
        => colors
            .Select((color, index) => (color, index))
            .GroupBy(entry => entry.color)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.First().index)
            .Select(group => group.Key)
            .FirstOrDefault();

    private sealed class Singer(string? voice)
    {
        public string? Voice { get; } = voice;
        public List<SungSpan> Spans { get; } = [];
        public List<LyricColor> Colors { get; } = [];
    }
}

/// <summary>One singer's stretches of a song, as <see cref="LyricLanes"/> reads them.</summary>
/// <param name="Voice">The singer, as <see cref="LyricPage.Voice"/> names them; null for the pages
/// that name nobody.</param>
/// <param name="Color">The colour the singer's words are lit in, or null to leave it to the theme.</param>
/// <param name="Spans">The stretches sung, in time order and never overlapping.</param>
public sealed record LyricLane(string? Voice, LyricColor? Color, IReadOnlyList<SungSpan> Spans);

/// <summary>A stretch of a song during which a singer is singing.</summary>
/// <param name="StartSeconds">Song position the stretch starts at.</param>
/// <param name="EndSeconds">Song position the stretch ends at; never before the start.</param>
public sealed record SungSpan(double StartSeconds, double EndSeconds);
