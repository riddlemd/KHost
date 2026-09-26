using KHost.Abstractions.Models;
using KHost.Abstractions.Models.Backgrounds;
using KHost.Abstractions.Services;
using KHost.Domain.Services.BurnIn;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute.ExceptionExtensions;

namespace KHost.UnitTests.Domain.Services.BurnIn;

/// <summary>Which of the venue's song backgrounds a burned-in song goes over, and that a background
/// is never the reason a song fails to play.</summary>
public class LyricBurnInTests
{
    private readonly ITimedLyricsService _lyrics = Substitute.For<ITimedLyricsService>();
    private readonly IVenuesService _venues = Substitute.For<IVenuesService>();
    private readonly IBackgroundPackService _backgrounds = Substitute.For<IBackgroundPackService>();
    private readonly IBurnInStreamService _streams = Substitute.For<IBurnInStreamService>();

    public LyricBurnInTests()
    {
        _lyrics.GetTimedLyricsAsync(default!, default).ReturnsForAnyArgs(new TimedLyrics
        {
            DurationSeconds = 10,
            Bounds = new LyricBox(0, 0, 640, 360),
            Pages = [new LyricPage { ShowFromSeconds = 0, ShowUntilSeconds = 5 }],
        });
        _backgrounds.ReadAsync(default).ReturnsForAnyArgs(new BackgroundPack
        {
            Entries =
            [
                new BackgroundPackEntry { File = "a.mp4", Name = "A", FilePath = "/packs/a.mp4" },
                new BackgroundPackEntry { File = "b.mp4", Name = "B", FilePath = "/packs/b.mp4" },
            ],
        });
    }

    [Fact]
    public async Task OpenAsync_UsesOnlyTheBackgroundsTheVenueTicked()
    {
        Ticked("B.MP4");

        await BurnIn(pick: _ => 0).OpenAsync(Request());

        await ReceivedBackground("/packs/b.mp4");
    }

    [Fact]
    public async Task OpenAsync_AVenueWithNoneTicked_PaintsOverBlack()
    {
        Ticked();

        await BurnIn(pick: _ => 0).OpenAsync(Request());

        await ReceivedBackground(null);
    }

    /// <summary>A choice left behind by a deleted clip resolves to nothing, not to a bad path.</summary>
    [Fact]
    public async Task OpenAsync_AChoiceTheFolderNoLongerHolds_IsNotUsed()
    {
        Ticked("gone.mp4");

        await BurnIn(pick: _ => 0).OpenAsync(Request());

        await ReceivedBackground(null);
    }

    [Fact]
    public async Task OpenAsync_AFolderThatCannotBeRead_StillOpensTheSong()
    {
        Ticked("a.mp4");
        _backgrounds.ReadAsync(default).ThrowsAsyncForAnyArgs(new IOException("gone"));

        await BurnIn(pick: _ => 0).OpenAsync(Request());

        await ReceivedBackground(null);
    }

    private void Ticked(params string[] files)
    {
        var venue = new Venue { Name = "Room" };
        venue.Settings.SongBackgrounds = [.. files];
        _venues.ReadSelectedVenueAsync().Returns(venue);
    }

    private Task ReceivedBackground(string? path)
        => _streams.Received(1).OpenBurningInAsync(
            Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<AudioMix?>(),
            Arg.Any<TimedLyrics>(), Arg.Is<string?>(background => background == path), Arg.Any<CancellationToken>());

    private LyricBurnIn BurnIn(Func<int, int> pick)
        => new(_lyrics, _venues, _backgrounds, _streams, NullLogger<LyricBurnIn>.Instance) { PickBackgroundIndex = pick };

    private static MediaRenderRequest Request() => new()
    {
        FilePath = "/songs/a.mka",
        Target = new RenderTarget { BurnLyrics = true },
    };
}
