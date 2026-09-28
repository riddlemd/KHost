using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace KHost.UnitTests.Domain.Services;

/// <summary>Choosing who answers for a file, and what happens when nobody can.</summary>
public class TimedLyricsServiceTests
{
    private const string SourceFile = "/library/plugin/6229.stems";

    private static TimedLyrics SomeLyrics() => new()
    {
        DurationSeconds = 120,
        Bounds = new LyricBox(0, 0, 640, 360),
    };

    private static ITimedLyricsProvider Provider(bool claims, TimedLyrics? answer = null)
    {
        var provider = Substitute.For<ITimedLyricsProvider>();
        provider.CanProvide(Arg.Any<string>()).Returns(claims);
        provider.GetTimedLyricsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(answer);
        return provider;
    }

    private readonly PlaybackService.ServiceOptions _options = new();

    private TimedLyricsService Service(params ITimedLyricsProvider[] providers)
    {
        var monitor = Substitute.For<IOptionsMonitor<PlaybackService.ServiceOptions>>();
        monitor.CurrentValue.Returns(_ => _options);
        return new(NullLogger<TimedLyricsService>.Instance, providers, monitor);
    }

    /// <summary>One opener, sung after a 2.5s silence, on a page up long enough for a full run.</summary>
    private static TimedLyrics AfterASilence() => SomeLyrics() with
    {
        Pages =
        [
            new LyricPage { ShowFromSeconds = 0, ShowUntilSeconds = 10, Lines = [new LyricLine { Position = new LyricBox(100, 100, 400, 50), Syllables = [new LyricSyllable(1, 2, "la")] }] },
            new LyricPage { ShowFromSeconds = 2, ShowUntilSeconds = 20, Lines = [new LyricLine { Position = new LyricBox(100, 100, 400, 50), Syllables = [new LyricSyllable(4.5, 5, "la")] }] },
        ],
    };

    [Fact]
    public async Task GetTimedLyricsAsync_AsksTheProviderThatClaimsTheFile()
    {
        var mine = Provider(claims: true, answer: SomeLyrics());
        var other = Provider(claims: false);

        var lyrics = await Service(other, mine).GetTimedLyricsAsync(SourceFile);

        Assert.NotNull(lyrics);
        await mine.Received(1).GetTimedLyricsAsync(SourceFile, Arg.Any<CancellationToken>());
        await other.DidNotReceive().GetTimedLyricsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetTimedLyricsAsync_FillsInLeadInsTheProviderLeftOut()
    {
        _options.DynamicLeadIns = true;

        var bare = SomeLyrics() with
        {
            Pages =
            [
                new LyricPage
                {
                    ShowFromSeconds = 10,
                    ShowUntilSeconds = 20,
                    Lines = [new LyricLine { Position = new LyricBox(100, 100, 400, 50), Syllables = [new LyricSyllable(15, 16, "la")] }],
                },
            ],
        };

        var lyrics = await Service(Provider(claims: true, answer: bare)).GetTimedLyricsAsync(SourceFile);

        // Both the screen and the burn-in read through here, so this is where they learn of it.
        Assert.NotNull(lyrics!.Pages[0].Lines[0].LeadIn);
    }

    [Fact]
    public async Task GetTimedLyricsAsync_Off_PassesTheProvidersLyricsThroughUntouched()
    {
        var answer = AfterASilence();

        var lyrics = await Service(Provider(claims: true, answer: answer)).GetTimedLyricsAsync(SourceFile);

        Assert.Same(answer, lyrics);
    }

    [Theory]
    [InlineData(2, true)]
    [InlineData(3, false)]
    public async Task GetTimedLyricsAsync_ThePauseSetting_DecidesWhichSilencesEarnOne(int pauseSeconds, bool expected)
    {
        _options.DynamicLeadIns = true;
        _options.DynamicLeadInPauseSeconds = pauseSeconds;

        // Two lines on one page, so only the setting's pause can earn the second one a lead-in.
        var answer = SomeLyrics() with
        {
            Pages =
            [
                new LyricPage
                {
                    ShowFromSeconds = 0,
                    ShowUntilSeconds = 20,
                    Lines =
                    [
                        new LyricLine { Position = new LyricBox(100, 100, 400, 50), Syllables = [new LyricSyllable(0.2, 2, "la")] },
                        new LyricLine { Position = new LyricBox(100, 160, 400, 50), Syllables = [new LyricSyllable(4.5, 5, "la")] },
                    ],
                },
            ],
        };

        var lyrics = await Service(Provider(claims: true, answer: answer)).GetTimedLyricsAsync(SourceFile);

        Assert.Equal(expected, lyrics!.Pages[0].Lines[1].LeadIn is not null);
    }

    [Fact]
    public async Task GetTimedLyricsAsync_AnswersNull_WhenNobodyClaimsTheFile()
    {
        var lyrics = await Service(Provider(claims: false), Provider(claims: false))
            .GetTimedLyricsAsync("/library/song.mp4");

        // The ordinary case, not a failure: almost nothing carries timing, and those songs play
        // with whatever picture the host already streams.
        Assert.Null(lyrics);
    }

    [Fact]
    public async Task GetTimedLyricsAsync_SkipsAProviderThatThrowsDeciding()
    {
        var broken = Substitute.For<ITimedLyricsProvider>();
        broken.CanProvide(Arg.Any<string>()).Throws(new InvalidOperationException("boom"));
        var mine = Provider(claims: true, answer: SomeLyrics());

        var lyrics = await Service(broken, mine).GetTimedLyricsAsync(SourceFile);

        // One plugin having a bad day must not cost the song its words.
        Assert.NotNull(lyrics);
    }

    [Fact]
    public async Task GetTimedLyricsAsync_AnswersNull_WhenTheOwnerCannotReadItsOwnFile()
    {
        var broken = Substitute.For<ITimedLyricsProvider>();
        broken.CanProvide(Arg.Any<string>()).Returns(true);
        broken.GetTimedLyricsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidDataException("not a real file"));
        var later = Provider(claims: true, answer: SomeLyrics());

        var lyrics = await Service(broken, later).GetTimedLyricsAsync(SourceFile);

        // Nobody else can read a container its owner could not, so the search stops rather than
        // handing the file to a provider that would answer about a format it never wrote.
        Assert.Null(lyrics);
        await later.DidNotReceive().GetTimedLyricsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetTimedLyricsAsync_LetsCancellationThrough()
    {
        var provider = Substitute.For<ITimedLyricsProvider>();
        provider.CanProvide(Arg.Any<string>()).Returns(true);
        provider.GetTimedLyricsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(new OperationCanceledException());

        // A cancelled load is not a plugin that failed, and swallowing it would report "no words"
        // for a song that was simply abandoned.
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => Service(provider).GetTimedLyricsAsync(SourceFile));
    }
}
