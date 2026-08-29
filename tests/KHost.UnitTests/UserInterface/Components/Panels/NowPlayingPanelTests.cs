using Microsoft.Extensions.Logging.Abstractions;
using KHost.Plugins.Sdk.Messaging;
using KHost.Domain.Services.Messaging;
using Bunit;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Plugins.Sdk.Services;
using KHost.UserInterface.Components.Panels;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;

namespace KHost.UnitTests.UserInterface.Components.Panels;

public class NowPlayingPanelTests : BunitContext
{
    private const string ArtistSelector = ".kh-now-playing__artist";

    private readonly IPlaybackService _playback = Substitute.For<IPlaybackService>();
    private readonly IBreakMusicService _breakMusic = Substitute.For<IBreakMusicService>();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);

    public NowPlayingPanelTests()
    {
        // The panel calls into JS to build a seek bar on first render; none of it matters here.
        JSInterop.Mode = JSRuntimeMode.Loose;

        _playback.State.Returns(PlaybackState.Playing);
        _playback.Position.Returns(TimeSpan.Zero);

        Services.AddSingleton(_playback);
        Services.AddSingleton<IMessageBroker>(_broker);
        Services.AddSingleton(Substitute.For<ISingerQueueService>());
        Services.AddSingleton(Substitute.For<IDialogService>());

        // The break music controls ride this panel's header, so their services have to resolve
        // even in tests that only care about the song. The substitute names no provider, which is
        // what keeps the bar from rendering into these assertions.
        Services.AddSingleton(_breakMusic);
        Services.AddSingleton(Substitute.For<IAdService>());
        Services.AddSingleton(Substitute.For<IFlashService>());
    }

    /// <summary>
    /// The break music controls ride this panel's header rather than a strip of their own, so the
    /// console does not give up a row to three buttons.
    /// </summary>
    [Fact]
    public void BreakMusicControls_RenderInsideThisPanelsHeader()
    {
        _breakMusic.ActiveProvider.Returns(Substitute.For<IBreakMusicProvider>());

        Load(Performance(), MediaWithArtist("Toto", "Africa"));

        var cut = Render<NowPlayingPanel>();

        Assert.NotEmpty(cut.FindAll(".kh-card__header .kh-break-music-bar .kh-break-music-bar__controls button"));
    }

    [Fact]
    public void BreakMusicControls_AreNotRenderedOutsideTheHeader()
    {
        _breakMusic.ActiveProvider.Returns(Substitute.For<IBreakMusicProvider>());

        Load(Performance(), MediaWithArtist("Toto", "Africa"));

        var cut = Render<NowPlayingPanel>();

        // Every bar the panel renders has to be the one in the header — a second, or a stray one
        // in the body, would be the old strip come back.
        Assert.Equal(
            cut.FindAll(".kh-break-music-bar").Count,
            cut.FindAll(".kh-card__header .kh-break-music-bar").Count);
    }

    [Fact]
    public void ArtistRenders_WhenTheMediaHasOne()
    {
        Load(Performance(), MediaWithArtist("Toto", "Africa"));

        var cut = Render<NowPlayingPanel>();

        Assert.Equal("Toto", cut.Find(ArtistSelector).TextContent);
    }

    [Fact]
    public void ArtistElementIsAbsent_WhenTheMediaHasNoArtist()
    {
        Load(Performance(), MediaWithArtist("", "Africa"));

        var cut = Render<NowPlayingPanel>();

        Assert.Empty(cut.FindAll(ArtistSelector));
    }

    [Fact]
    public void KeyControl_RaisesAndLowersTheKey()
    {
        Load(Performance(), MediaWithArtist("Toto", "Africa"));
        _playback.Pitch.Returns(0);

        var cut = Render<NowPlayingPanel>();
        var buttons = cut.FindAll(".kh-now-playing__key-btn");

        buttons[1].Click();
        buttons[0].Click();

        _playback.Received(1).SetPitchAsync(1);
        _playback.Received(1).SetPitchAsync(-1);
    }

    [Fact]
    public void KeyControl_ShowsTheShiftAgainstTheRecording()
    {
        Load(Performance(), MediaWithArtist("Toto", "Africa"));
        _playback.Pitch.Returns(-2);

        var cut = Render<NowPlayingPanel>();

        // A host glancing at the console has to see it is not the written key without reading it.
        var value = cut.Find(".kh-now-playing__key-value");
        Assert.Equal("\u22122", value.TextContent.Trim());
        Assert.Contains("kh-now-playing__key-value--shifted", value.ClassName);
    }

    [Fact]
    public void KeyControl_IsNotMarkedShifted_InTheWrittenKey()
    {
        Load(Performance(), MediaWithArtist("Toto", "Africa"));
        _playback.Pitch.Returns(0);

        var cut = Render<NowPlayingPanel>();

        var value = cut.Find(".kh-now-playing__key-value");
        Assert.Equal("0", value.TextContent.Trim());
        Assert.DoesNotContain("--shifted", value.ClassName);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(-6)]
    public void KeyControl_DisablesTheButtonAtTheEndOfTheRange(int pitch)
    {
        Load(Performance(), MediaWithArtist("Toto", "Africa"));
        _playback.Pitch.Returns(pitch);

        var cut = Render<NowPlayingPanel>();
        var buttons = cut.FindAll(".kh-now-playing__key-btn");

        // The far end stays live; only the one that would leave the range is off.
        var expectedDisabled = pitch > 0 ? 1 : 0;
        Assert.True(buttons[expectedDisabled].HasAttribute("disabled"));
        Assert.False(buttons[1 - expectedDisabled].HasAttribute("disabled"));
    }

    [Fact]
    public void TempoControl_SpeedsUpAndSlowsDown()
    {
        Load(Performance(), MediaWithArtist("Toto", "Africa"));
        _playback.Tempo.Returns(0);

        var cut = Render<NowPlayingPanel>();
        var buttons = cut.FindAll(".kh-now-playing__tempo .kh-now-playing__key-btn");

        buttons[1].Click();
        buttons[0].Click();

        _playback.Received(1).SetTempoAsync(5);
        _playback.Received(1).SetTempoAsync(-5);
    }

    [Fact]
    public void TempoControl_ShowsTheSpeedAsAPercentage()
    {
        Load(Performance(), MediaWithArtist("Toto", "Africa"));
        _playback.Tempo.Returns(-15);

        var cut = Render<NowPlayingPanel>();

        var value = cut.Find(".kh-now-playing__tempo .kh-now-playing__key-value");
        Assert.Equal("\u221215%", value.TextContent.Trim());
        Assert.Contains("kh-now-playing__key-value--shifted", value.ClassName);
    }

    [Theory]
    [InlineData(50)]
    [InlineData(-50)]
    public void TempoControl_DisablesTheButtonAtTheEndOfTheRange(int tempo)
    {
        Load(Performance(), MediaWithArtist("Toto", "Africa"));
        _playback.Tempo.Returns(tempo);

        var cut = Render<NowPlayingPanel>();
        var buttons = cut.FindAll(".kh-now-playing__tempo .kh-now-playing__key-btn");

        var expectedDisabled = tempo > 0 ? 1 : 0;
        Assert.True(buttons[expectedDisabled].HasAttribute("disabled"));
        Assert.False(buttons[1 - expectedDisabled].HasAttribute("disabled"));
    }

    private void Load(Performance performance, Media media)
    {
        _playback.CurrentPerformance.Returns(performance);
        _playback.CurrentMedia.Returns(media);
    }

    private static Performance Performance() => new() { SingerId = Guid.NewGuid() };

    private static Media MediaWithArtist(string artist, string title) => new()
    {
        FilePath = "song.mp4",
        Title = title,
        Artist = artist,
    };
}
