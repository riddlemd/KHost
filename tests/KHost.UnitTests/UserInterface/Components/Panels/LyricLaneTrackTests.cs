using Bunit;
using KHost.Common.Lyrics;
using KHost.UserInterface.Components.Panels;

namespace KHost.UnitTests.UserInterface.Components.Panels;

/// <summary>The lanes are hundreds of rects for a long duet; the panel above re-renders on every
/// position tick, and the lanes must only redraw when what they show has moved.</summary>
public class LyricLaneTrackTests : BunitContext
{
    private const string SpanSelector = ".kh-now-playing__lanes--by-voice .kh-now-playing__lane-span";

    private readonly IReadOnlyList<LyricLane> _lanes = [new LyricLane("a", null, [new SungSpan(0, 10)])];
    private readonly LyricLane _oneLane = new(null, null, [new SungSpan(0, 10)]);
    private readonly Guid _mediaId = Guid.NewGuid();

    [Fact]
    public void TheSameParameters_DoNotRedraw()
    {
        var cut = RenderTrack();
        var before = cut.RenderCount;

        cut.Render(p => p.Add(x => x.Lanes, _lanes).Add(x => x.OneLane, _oneLane)
            .Add(x => x.MediaId, _mediaId).Add(x => x.DurationSeconds, 100.0));

        Assert.Equal(before, cut.RenderCount);
    }

    [Fact]
    public void ANewLaneList_Redraws()
    {
        var cut = RenderTrack();

        cut.Render(p => p.Add(x => x.Lanes, [new LyricLane("a", null, [new SungSpan(0, 10), new SungSpan(20, 30)])]));

        Assert.Equal(2, cut.FindAll(SpanSelector).Count);
    }

    [Fact]
    public void ANewOneLane_Redraws()
    {
        var cut = RenderTrack();

        cut.Render(p => p.Add(x => x.OneLane, new LyricLane(null, null, [new SungSpan(0, 10), new SungSpan(20, 30)])));

        Assert.Equal(2, cut.FindAll(".kh-now-playing__lanes--one .kh-now-playing__lane-span").Count);
    }

    [Fact]
    public void ANewMedia_Redraws()
    {
        var cut = RenderTrack();
        var before = cut.RenderCount;

        cut.Render(p => p.Add(x => x.MediaId, Guid.NewGuid()));

        Assert.Equal(before + 1, cut.RenderCount);
    }

    [Fact]
    public void ANewDuration_Redraws()
    {
        var cut = RenderTrack();

        cut.Render(p => p.Add(x => x.DurationSeconds, 200.0));

        Assert.Equal("5%", cut.Find(SpanSelector).GetAttribute("width"));
    }

    private IRenderedComponent<LyricLaneTrack> RenderTrack() => Render<LyricLaneTrack>(p => p
        .Add(x => x.Lanes, _lanes)
        .Add(x => x.OneLane, _oneLane)
        .Add(x => x.MediaId, _mediaId)
        .Add(x => x.DurationSeconds, 100.0));
}
