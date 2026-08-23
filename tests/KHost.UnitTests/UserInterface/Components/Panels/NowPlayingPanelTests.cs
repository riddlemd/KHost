using Bunit;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.UserInterface.Components.Panels;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;

namespace KHost.UnitTests.UserInterface.Components.Panels;

public class NowPlayingPanelTests : BunitContext
{
    private const string ArtistSelector = ".kh-now-playing__artist";

    private readonly IPlaybackService _playback = Substitute.For<IPlaybackService>();

    public NowPlayingPanelTests()
    {
        // The panel calls into JS to build a seek bar on first render; none of it matters here.
        JSInterop.Mode = JSRuntimeMode.Loose;

        _playback.State.Returns(PlaybackState.Playing);
        _playback.Position.Returns(TimeSpan.Zero);

        Services.AddSingleton(_playback);
        Services.AddSingleton(Substitute.For<ISingerQueueService>());
        Services.AddSingleton(Substitute.For<IDialogService>());
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
