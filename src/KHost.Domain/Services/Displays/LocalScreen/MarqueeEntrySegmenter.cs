using KHost.Abstractions.Models;
using KHost.Common.Display;
using KHost.IPC.SignalR.Contracts;

namespace KHost.Domain.Services.Displays.LocalScreen;

/// <summary>Splits one upcoming turn's wording into coloured runs, host-side, so a display never
/// has to parse <see cref="Venue.VenueSettings.MarqueeEntryFormat"/> itself.</summary>
/// <remarks>Kept out of <c>KHost.Common</c> deliberately: <see cref="MarqueeSegment"/> is an IPC wire
/// type, not a plugin contract, and folding this in with <see cref="MarqueeText"/> would pull a
/// published-package shape change for a display nothing but the local screen currently colours.
/// Reuses <see cref="MarqueeText"/>'s tags and default format, so the two can never disagree on what
/// a turn says — only on how it is coloured.</remarks>
internal static class MarqueeEntrySegmenter
{
    /// <summary>One upcoming turn's segments, in reading order. A singer with nothing queued is one
    /// <see cref="MarqueeSegmentKind.Singer"/> segment, matching <see cref="MarqueeText.ComposeEntry"/>.</summary>
    public static IReadOnlyList<MarqueeSegment> ComposeSegments(UpNextEntry entry, string? entryFormat)
    {
        if (entry.Title is null)
            return [new MarqueeSegment { Text = entry.Singer, Kind = MarqueeSegmentKind.Singer }];

        var format = string.IsNullOrWhiteSpace(entryFormat) ? MarqueeText.DefaultEntryFormat : entryFormat;

        (string Tag, MarqueeSegmentKind Kind, string Value)[] tags =
        [
            ("{song}", MarqueeSegmentKind.Song, entry.Title),
            ("{artist}", MarqueeSegmentKind.Song, entry.Artist ?? ""),
            ("{singer}", MarqueeSegmentKind.Singer, entry.Singer),
            ("{position}", MarqueeSegmentKind.Other, entry.Position.ToString()),
        ];

        var segments = new List<MarqueeSegment>();
        var pos = 0;

        while (pos < format.Length)
        {
            var bestIndex = -1;
            var bestLength = 0;
            var bestKind = MarqueeSegmentKind.Other;
            var bestValue = "";

            // Earliest tag wins: a format may use them in any order, or not at all.
            foreach (var (tag, kind, value) in tags)
            {
                var index = format.IndexOf(tag, pos, StringComparison.OrdinalIgnoreCase);
                if (index < 0 || (bestIndex != -1 && index >= bestIndex)) continue;

                bestIndex = index;
                bestLength = tag.Length;
                bestKind = kind;
                bestValue = value;
            }

            if (bestIndex == -1)
            {
                segments.Add(new MarqueeSegment { Text = format[pos..], Kind = MarqueeSegmentKind.Other });
                break;
            }

            if (bestIndex > pos)
                segments.Add(new MarqueeSegment { Text = format[pos..bestIndex], Kind = MarqueeSegmentKind.Other });

            // An empty tag value (no artist recorded) draws nothing rather than an empty run.
            if (bestValue.Length > 0)
                segments.Add(new MarqueeSegment { Text = bestValue, Kind = bestKind });

            pos = bestIndex + bestLength;
        }

        return segments;
    }

    /// <summary>The glyph a chosen <see cref="MarqueeDividerShape"/> draws; null for "None", which is
    /// no divider at all rather than an empty one.</summary>
    public static string? ResolveGlyph(MarqueeDividerShape shape) => shape switch
    {
        MarqueeDividerShape.Dot => "•",
        MarqueeDividerShape.Diamond => "◆",
        MarqueeDividerShape.Star => "★",
        MarqueeDividerShape.Slash => "/",
        MarqueeDividerShape.Pipe => "|",
        MarqueeDividerShape.Note => "♪",
        _ => null,
    };
}
