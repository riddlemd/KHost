using Microsoft.AspNetCore.Components;
using KHost.Common.Lyrics;

namespace KHost.UserInterface.Components.Panels;

/// <summary>Who sings where across the song: one rect per sung stretch, which for a long duet is
/// hundreds of elements. Kept apart from the playhead so the position clock never re-diffs it.</summary>
public partial class LyricLaneTrack
{
    private IReadOnlyList<LyricLane>? _drawnLanes;
    private LyricLane? _drawnOneLane;
    private Guid? _drawnMediaId;
    private double _drawnDurationSeconds;
    private bool _changed = true;

    [Parameter, EditorRequired] public IReadOnlyList<LyricLane> Lanes { get; set; } = [];
    [Parameter] public LyricLane? OneLane { get; set; }
    [Parameter, EditorRequired] public Guid? MediaId { get; set; }
    [Parameter, EditorRequired] public double DurationSeconds { get; set; }

    /// <summary>A position as a percentage of the song, for an SVG coordinate.</summary>
    public static string PercentOf(double seconds, double durationSeconds)
        => Percent(Math.Clamp(seconds / durationSeconds * 100, 0, 100));

    // A list parameter is never "known unchanged" to Blazor, so every parent render would redraw
    // the lanes; a re-read hands over a new list, which is what this compares.
    protected override void OnParametersSet()
    {
        _changed = !ReferenceEquals(Lanes, _drawnLanes) || !ReferenceEquals(OneLane, _drawnOneLane)
            || MediaId != _drawnMediaId || !DurationSeconds.Equals(_drawnDurationSeconds);

        _drawnLanes = Lanes;
        _drawnOneLane = OneLane;
        _drawnMediaId = MediaId;
        _drawnDurationSeconds = DurationSeconds;
    }

    protected override bool ShouldRender() => _changed;

    /// <summary>Where lane <paramref name="index"/> of <paramref name="count"/> sits in the track,
    /// as SVG percentages. A lone lane is half the track's height; stacked
    /// lanes take more of their band so each stays thick enough to see.</summary>
    private static (string Y, string Height) LaneBand(int index, int count)
    {
        var band = 100.0 / count;
        var height = band * (count == 1 ? 0.5 : 0.7);
        var y = band * index + (band - height) / 2;

        return (Percent(y), Percent(height));
    }

    private static string Percent(double value)
        => value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "%";

    private static string? FillOf(LyricLane lane)
        => lane.Color is { } c ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : null;
}
