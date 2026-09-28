using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services;
using KHost.Domain.Services.Messaging;
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
        return new(NullLogger<TimedLyricsService>.Instance, providers, monitor, Substitute.For<IMessageBroker>());
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

    /// <summary>A duet whose unsung tints a protanope and a deuteranope take for one another.</summary>
    private static TimedLyrics ConfusableDuet() => SomeLyrics() with
    {
        Pages =
        [
            new LyricPage { ShowFromSeconds = 0, ShowUntilSeconds = 10, Voice = "Kid Rock", Active = new LyricColor(0x0B, 0x96, 0xCA), Inactive = new LyricColor(0xD7, 0xF2, 0xFD) },
            new LyricPage { ShowFromSeconds = 5, ShowUntilSeconds = 15, Voice = "Sherly Crow", Active = new LyricColor(0xF5, 0x2C, 0x77), Inactive = new LyricColor(0xFD, 0xD7, 0xE6) },
        ],
    };

    [Fact]
    public async Task GetTimedLyricsAsync_ColorBlindFriendlyOff_LeavesConfusableColoursAlone()
    {
        var answer = ConfusableDuet();

        var lyrics = await Service(Provider(claims: true, answer: answer)).GetTimedLyricsAsync(SourceFile);

        Assert.Same(answer, lyrics);
    }

    [Fact]
    public async Task GetTimedLyricsAsync_ColorBlindFriendlyOn_SeparatesConfusableColours()
    {
        _options.ColorBlindFriendlyLyrics = true;

        var lyrics = await Service(Provider(claims: true, answer: ConfusableDuet())).GetTimedLyricsAsync(SourceFile);

        // Both the screen and the burn-in read through here, so both draw the moved colours.
        Assert.Equal(new LyricColor(0xFB, 0xFE, 0xFF), lyrics!.Pages[0].Inactive);
        Assert.Equal(new LyricColor(0xE2, 0xBD, 0xCC), lyrics.Pages[1].Inactive);
    }

    /// <summary>Timing no adjustment can make sense of still reaches the screen, as the provider gave it.</summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task GetTimedLyricsAsync_AnAdjustmentThatThrows_KeepsTheWords(bool colorBlind, bool leadIns)
    {
        _options.ColorBlindFriendlyLyrics = colorBlind;
        _options.DynamicLeadIns = leadIns;
        var unreadable = SomeLyrics() with { Pages = null! };

        var lyrics = await Service(Provider(claims: true, answer: unreadable)).GetTimedLyricsAsync(SourceFile);

        Assert.Same(unreadable, lyrics);
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

    // --- announcing a change to the adjustments ---

    /// <summary>An options monitor a test can reload, as the overlay's file watcher does.</summary>
    private sealed class ReloadingMonitor(PlaybackService.ServiceOptions value) : IOptionsMonitor<PlaybackService.ServiceOptions>
    {
        private readonly List<Action<PlaybackService.ServiceOptions, string?>> _listeners = [];

        public PlaybackService.ServiceOptions CurrentValue { get; set; } = value;

        public PlaybackService.ServiceOptions Get(string? name) => CurrentValue;

        public IDisposable OnChange(Action<PlaybackService.ServiceOptions, string?> listener)
        {
            _listeners.Add(listener);
            return new Unsubscriber(() => _listeners.Remove(listener));
        }

        public void Reload(PlaybackService.ServiceOptions next)
        {
            CurrentValue = next;
            foreach (var listener in _listeners.ToList()) listener(next, null);
        }

        private sealed class Unsubscriber(Action dispose) : IDisposable
        {
            public void Dispose() => dispose();
        }
    }

    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);

    /// <summary>Builds the service over a reloadable monitor and counts what it announces.</summary>
    private (ReloadingMonitor Monitor, Func<int> Announced, IDisposable Lifetime) Watched()
    {
        var monitor = new ReloadingMonitor(new PlaybackService.ServiceOptions());
        var service = new TimedLyricsService(
            NullLogger<TimedLyricsService>.Instance, [], monitor, _broker, TimeSpan.FromMilliseconds(20));

        var raised = 0;
        var subscription = _broker.Subscribe<TimedLyricsSettingsChanged>(_ => Interlocked.Increment(ref raised));

        return (monitor, () => Volatile.Read(ref raised), new CompositeLifetime(service, subscription));
    }

    private sealed class CompositeLifetime(params IDisposable[] parts) : IDisposable
    {
        public void Dispose() { foreach (var part in parts) part.Dispose(); }
    }

    /// <summary>Waits out the settle and the broker's own hand-off, long past either.</summary>
    private static async Task SettledAsync(Func<int> announced, int atLeast)
    {
        for (var i = 0; i < 100 && announced() < atLeast; i++) await Task.Delay(20);
        await Task.Delay(150);
    }

    public static TheoryData<string> Adjustments => ["ColorBlind", "LeadIns", "Pause"];

    [Theory]
    [MemberData(nameof(Adjustments))]
    public async Task OptionsReloaded_AnAdjustmentMoved_AnnouncesOnce(string which)
    {
        var (monitor, announced, lifetime) = Watched();
        using var _ = lifetime;

        // The pause counts only while lead-ins are on, so that case starts from on.
        if (which == "Pause")
        {
            monitor.Reload(new PlaybackService.ServiceOptions { DynamicLeadIns = true });
            await SettledAsync(announced, 1);
        }

        var before = announced();
        var next = new PlaybackService.ServiceOptions
        {
            ColorBlindFriendlyLyrics = which == "ColorBlind",
            DynamicLeadIns = which is "LeadIns" or "Pause",
            DynamicLeadInPauseSeconds = which == "Pause" ? 5 : LeadInGenerator.DefaultLongPauseSeconds,
        };

        // A file watcher raises a save more than once.
        monitor.Reload(next);
        monitor.Reload(next);
        await SettledAsync(announced, before + 1);

        Assert.Equal(before + 1, announced());
    }

    [Fact]
    public async Task OptionsReloaded_SeveralMovedInOneSave_AnnouncesOnce()
    {
        var (monitor, announced, lifetime) = Watched();
        using var _ = lifetime;

        // Read part-written first, as a reload can: defaults, then the whole save.
        monitor.Reload(new PlaybackService.ServiceOptions { ColorBlindFriendlyLyrics = true });
        monitor.Reload(new PlaybackService.ServiceOptions
        {
            ColorBlindFriendlyLyrics = true,
            DynamicLeadIns = true,
            DynamicLeadInPauseSeconds = 2,
        });
        await SettledAsync(announced, 1);

        Assert.Equal(1, announced());
    }

    [Fact]
    public async Task OptionsReloaded_OnlyAnUnrelatedSettingMoved_AnnouncesNothing()
    {
        var (monitor, announced, lifetime) = Watched();
        using var _ = lifetime;

        monitor.Reload(new PlaybackService.ServiceOptions { StopFadeDuration = TimeSpan.FromSeconds(1), LeadInGraceSeconds = 5 });
        await SettledAsync(announced, 1);

        Assert.Equal(0, announced());
    }

    [Fact]
    public async Task OptionsReloaded_ThePauseMovedWithLeadInsOff_AnnouncesNothing()
    {
        var (monitor, announced, lifetime) = Watched();
        using var _ = lifetime;

        // No word changes: with lead-ins off, the pause is never read.
        monitor.Reload(new PlaybackService.ServiceOptions { DynamicLeadInPauseSeconds = 5 });
        await SettledAsync(announced, 1);

        Assert.Equal(0, announced());
    }
}
