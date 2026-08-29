using Bunit;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services.Messaging;
using KHost.Plugins.Sdk.Messaging;
using KHost.UserInterface.Components.Panels;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.UserInterface.Components.Panels;

/// <summary>
/// Key and tempo open into the Now Playing card from its header. Both are sliders, and a drag
/// emits a value per pixel — so what reaches the service is the value the host let go of, not
/// every one they passed over on the way.
/// </summary>
public class SongControlsTests : BunitContext
{
    private const string Trigger = ".kh-song-controls__trigger";
    private const string Sliders = ".kh-song-controls__slider";
    private const string Values = ".kh-song-controls__value";

    private readonly IPlaybackService _playback = Substitute.For<IPlaybackService>();

    public SongControlsTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        Services.AddSingleton(_playback);
        Services.AddSingleton<IMessageBroker>(new MessageBroker(NullLogger<MessageBroker>.Instance));
    }

    [Fact]
    public void Dragging_TellsTheServiceOnlyTheValueLetGoOf()
    {
        var cut = Open();
        var key = cut.FindAll(Sliders)[0];

        key.Input("2");
        key.Input("4");
        key.Change("4");

        // Each value the service hears is a database write, an announcement and a settle restarted.
        _playback.DidNotReceive().SetPitchAsync(2);
        _playback.Received(1).SetPitchAsync(4);
    }

    [Fact]
    public void Dragging_MovesTheReadoutBeforeItIsLetGoOf()
    {
        var cut = Open();

        cut.FindAll(Sliders)[0].Input("-3");

        // The thumb is under the host's finger; a readout that waited for the settle would lag it.
        Assert.Equal("−3", cut.FindAll(Values)[0].TextContent.Trim());
    }

    [Fact]
    public void TempoSlider_CommitsAPercentage()
    {
        var cut = Open();
        var tempo = cut.FindAll(Sliders)[1];

        tempo.Change("-20");

        _playback.Received(1).SetTempoAsync(-20);
        Assert.Equal("−20%", cut.FindAll(Values)[1].TextContent.Trim());
    }

    [Fact]
    public void Reopening_DiscardsADragThatWasNeverLetGoOf()
    {
        _playback.Pitch.Returns(-2);

        var cut = Open();
        cut.FindAll(Sliders)[0].Input("5");

        // Closed without a change event, so nothing was committed and nothing announced — the only
        // thing that puts the thumb back where the song actually is, is reading it again on open.
        cut.Find(Trigger).Click();
        cut.Find(Trigger).Click();

        Assert.Equal("−2", cut.FindAll(Values)[0].TextContent.Trim());
    }

    [Fact]
    public void Sliders_SpanTheRangeTheServiceAccepts()
    {
        var cut = Open();
        var sliders = cut.FindAll(Sliders);

        // A slider wider than the clamp lets a host drag to a value that silently snaps back.
        Assert.Equal("-6", sliders[0].GetAttribute("min"));
        Assert.Equal("6", sliders[0].GetAttribute("max"));
        Assert.Equal("-50", sliders[1].GetAttribute("min"));
        Assert.Equal("50", sliders[1].GetAttribute("max"));
        Assert.Equal("5", sliders[1].GetAttribute("step"));
    }

    [Fact]
    public void Readouts_StartFromWhatIsLoaded()
    {
        _playback.Pitch.Returns(-2);
        _playback.Tempo.Returns(15);

        var cut = Open();
        var values = cut.FindAll(Values);

        Assert.Equal("−2", values[0].TextContent.Trim());
        Assert.Equal("+15%", values[1].TextContent.Trim());
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(2, "kh-song-controls__value--up")]
    [InlineData(-2, "kh-song-controls__value--down")]
    public void Readout_TakesItsColourFromTheSign(int pitch, string? expected)
    {
        _playback.Pitch.Returns(pitch);

        var cut = Open();
        var className = cut.FindAll(Values)[0].ClassName ?? "";

        // The number and the bar must never disagree about which way the song moved.
        if (expected is null)
        {
            Assert.DoesNotContain("--up", className);
            Assert.DoesNotContain("--down", className);
        }
        else
        {
            Assert.Contains(expected, className);
        }
    }

    [Fact]
    public void Track_PaintsNothingAtRest()
    {
        _playback.Pitch.Returns(0);

        var cut = Open();

        // No line at zero: an untouched panel should read as untouched.
        Assert.Contains("--fill:transparent", cut.FindAll(Sliders)[0].GetAttribute("style"));
    }

    [Fact]
    public void Track_PaintsRightwardInGreen_AndLeftwardInRed()
    {
        _playback.Pitch.Returns(3);
        _playback.Tempo.Returns(-25);

        var cut = Open();
        var sliders = cut.FindAll(Sliders);

        // Key +3 of −6..+6: from the centre (0.5) rightward to 0.75.
        var key = sliders[0].GetAttribute("style")!;
        Assert.Contains("--from-frac:0.5000", key);
        Assert.Contains("--to-frac:0.7500", key);
        Assert.Contains("--fill:var(--kh-success)", key);

        // Tempo −25 of ±50: leftward from 0.25 back to the centre.
        var tempo = sliders[1].GetAttribute("style")!;
        Assert.Contains("--from-frac:0.2500", tempo);
        Assert.Contains("--to-frac:0.5000", tempo);
        Assert.Contains("--fill:var(--kh-danger)", tempo);
    }

    [Fact]
    public void Track_PaintsAVolumeFromTheLeft_NotTheMiddle()
    {
        GiveVocalTracks();
        _playback.LeadVolume.Returns(40);

        var cut = Open();
        var lead = cut.FindAll(Sliders)[2].GetAttribute("style")!;

        // A volume has no negative side, so its rest is the left edge.
        Assert.Contains("--from-frac:0.0000", lead);
        Assert.Contains("--to-frac:0.4000", lead);
        Assert.Contains("--fill:var(--kh-success)", lead);
    }

    [Fact]
    public void VocalRows_AreHidden_ForAnOrdinarySingleTrackSong()
    {
        var cut = Open();

        // Most songs carry one audio stream; offering faders for voices that are not there
        // would be two controls that do nothing.
        Assert.Equal(2, cut.FindAll(Sliders).Count);
    }

    [Fact]
    public void VocalRows_AppearForAFileThatShipsItsVoicesApart()
    {
        GiveVocalTracks();

        var cut = Open();

        Assert.Equal(4, cut.FindAll(Sliders).Count);
    }

    [Fact]
    public void VocalRows_ShowOnlyTheVoicesTheFileHas()
    {
        _playback.AudioTracks.Returns<IReadOnlyList<AudioTrack>>(
        [
            new AudioTrack(0, AudioTrackRole.Music, "Instrumental"),
            new AudioTrack(1, AudioTrackRole.Lead, "Lead Vocal"),
        ]);

        var cut = Open();

        // A file with no harmonies gets no backing fader.
        Assert.Equal(3, cut.FindAll(Sliders).Count);
        Assert.DoesNotContain("Backing", cut.Markup);
    }

    [Fact]
    public void VocalSliders_CommitOnRelease()
    {
        GiveVocalTracks();

        var cut = Open();
        var sliders = cut.FindAll(Sliders);

        sliders[2].Change("60");
        sliders[3].Change("35");

        _playback.Received(1).SetLeadVolumeAsync(60);
        _playback.Received(1).SetBackingVolumeAsync(35);
    }

    [Fact]
    public void Trigger_IsNotMarkedByTheVocalLevels()
    {
        GiveVocalTracks();
        _playback.CurrentPerformance.Returns(new Performance { SingerId = Guid.NewGuid() });
        _playback.LeadVolume.Returns(0);
        _playback.BackingVolume.Returns(100);

        var cut = Render<SongControls>();

        // Those are where every song starts, so counting them marks the trigger all night.
        Assert.Empty(cut.FindAll(".kh-song-controls__icon--on"));
    }

    private void GiveVocalTracks() =>
        _playback.AudioTracks.Returns<IReadOnlyList<AudioTrack>>(
        [
            new AudioTrack(0, AudioTrackRole.Music, "Instrumental"),
            new AudioTrack(1, AudioTrackRole.Backing, "Backing Vocal"),
            new AudioTrack(2, AudioTrackRole.Lead, "Lead Vocal"),
        ]);

    [Fact]
    public void ClosedTrigger_SaysWhenTheSongIsNotAsRecorded()
    {
        _playback.CurrentPerformance.Returns(new Performance { SingerId = Guid.NewGuid() });
        _playback.Pitch.Returns(2);

        var cut = Render<SongControls>();

        // A host who left the last song down two semitones will not think to open the panel.
        Assert.NotEmpty(cut.FindAll(".kh-song-controls__icon--on"));
        Assert.Contains("+2", cut.Find(Trigger).GetAttribute("title"));
    }

    [Fact]
    public void ClosedTrigger_IsUnmarked_ForASongAsRecorded()
    {
        _playback.CurrentPerformance.Returns(new Performance { SingerId = Guid.NewGuid() });

        var cut = Render<SongControls>();

        Assert.Empty(cut.FindAll(".kh-song-controls__icon--on"));
    }

    [Fact]
    public void Escape_ClosesThePanel()
    {
        var cut = Open();

        cut.Find(".kh-song-controls").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        Assert.Empty(cut.FindAll(Sliders));
    }

    [Fact]
    public void ClickingAway_ClosesThePanel()
    {
        var cut = Open();

        cut.Find(".kh-song-controls__scrim").Click();

        Assert.Empty(cut.FindAll(Sliders));
    }

    [Fact]
    public void Trigger_IsDisabled_WithNoSongLoaded()
    {
        var cut = Render<SongControls>();

        // There is nothing to transpose, and opening would offer controls that go nowhere.
        Assert.True(cut.Find(Trigger).HasAttribute("disabled"));
    }

    /// <summary>Renders with a song loaded and the panel open, which is where the sliders live.</summary>
    private IRenderedComponent<SongControls> Open()
    {
        _playback.CurrentPerformance.Returns(new Performance { SingerId = Guid.NewGuid() });

        var cut = Render<SongControls>();
        cut.Find(Trigger).Click();

        return cut;
    }
}
