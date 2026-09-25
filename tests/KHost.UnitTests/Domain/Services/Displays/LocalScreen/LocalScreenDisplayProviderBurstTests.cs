using KHost.Abstractions.Messaging.Messages;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.IPC.SignalR.Contracts;
using KHost.Domain.Services;
using KHost.Domain.Services.BreakMusic;
using KHost.Domain.Services.Displays.LocalScreen;
using KHost.Domain.Services.Messaging;
using KHost.Domain.Services.QrCodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KHost.UnitTests.Domain.Services.Displays.LocalScreen;

/// <summary>One host action announces from several services; the screen hears it as one draw.</summary>
/// <remarks>Real broker, real provider and real producers where the burst is theirs, over a
/// substituted server, which is what counts what the screen was sent.</remarks>
public class LocalScreenDisplayProviderBurstTests : IDisposable
{
    // Wide, so a loaded machine still lands every step of a stop inside one window; the fade
    // outlasts it, so the Stopping redraw is its own burst.
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan Fade = TimeSpan.FromMilliseconds(700);

    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);
    private readonly IScreenServer _screenServer = Substitute.For<IScreenServer>();
    private readonly IUpNextService _named = Substitute.For<IUpNextService>();
    private readonly IQrCodeOfferService _qrCodes = Substitute.For<IQrCodeOfferService>();
    private readonly IVenuesService _venues = Substitute.For<IVenuesService>();
    private readonly ISingerQueueService _queue = Substitute.For<ISingerQueueService>();
    private readonly IPerformanceService _performances = Substitute.For<IPerformanceService>();
    private readonly IMediaStreamService _streams = Substitute.For<IMediaStreamService>();
    private readonly IMediaGateService _gate = Substitute.For<IMediaGateService>();
    private readonly List<IDisposable> _built = [];

    // Who the marquee names: Ada is singing, so she is left out until her turn ends.
    private volatile string _upNext = "Bo, Cy";

    public LocalScreenDisplayProviderBurstTests()
    {
        // The marquee reads who is next here; the real UpNextService below only times the announcing.
        _named.ReadAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ => (IReadOnlyList<UpNextEntry>)[.. _upNext.Split(", ").Select((singer, i) => new UpNextEntry { Position = i + 1, Singer = singer })]);
        _qrCodes.ReadOfferAsync(Arg.Any<CancellationToken>()).Returns((QrCodeOffer?)null);
        _venues.ReadSelectedVenueAsync().Returns(new Venue { Name = "The Bar", Settings = new Venue.VenueSettings { MarqueeEnabled = true, MarqueeSingerCount = 5 } });
        _gate.EvaluateAsync(Arg.Any<MediaAction>(), Arg.Any<Media>(), Arg.Any<CancellationToken>()).Returns(PlaybackGateResult.Ok);
        _streams
            .OpenAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<AudioMix?>(), Arg.Any<CancellationToken>())
            .Returns(call => new MediaStreamSession
            {
                Id = "stream-1",
                SourcePath = call.ArgAt<string>(0),
                PlaylistUrl = "http://host/media/stream-1/stream.m3u8",
                StartOffset = call.ArgAt<TimeSpan>(1),
                Pitch = call.ArgAt<int>(2),
                Tempo = call.ArgAt<int>(3),
            });

        // Each announces as the real service does, and leaves the queue where it really would:
        // after the dequeue Ada is back at the top, and only the rotation sends her to the end.
        _performances.DequeueAsync(Arg.Any<Guid>(), Arg.Any<Guid>()).Returns(_ =>
        {
            _upNext = "Ada, Bo, Cy";
            _broker.Announce(new PerformancesChanged());
            return Task.CompletedTask;
        });
        _queue.RotateQueueAsync(Arg.Any<Guid>()).Returns(_ => RotateAsync());
    }

    public void Dispose()
    {
        foreach (var built in _built)
            built.Dispose();
    }

    /// <summary>The live smoke test's bug: a stop sent the marquee three times within 17ms, one
    /// per announcement (PerformancesChanged, SingerQueueChanged, PlaybackChanged).</summary>
    [Fact]
    public async Task StopAsync_MidSong_SendsTheMarqueeOnce_ForWhereTheTurnEnded()
    {
        var playback = PlaybackOverTheScreen(Substitute.For<IBreakMusicService>());
        Connect("conn-a");

        var (performance, media) = Song();
        await playback.LoadAsync(performance, media);
        await playback.PlayAsync();

        Assert.True(await WaitForSentAsync<SetMarqueeCommand>(marquee => Named(marquee) == "Bo, Cy"));
        await Task.Delay(Settle * 2);
        _screenServer.ClearReceivedCalls();

        await playback.StopAsync();

        Assert.True(await WaitForSentAsync<SetMarqueeCommand>(marquee => Named(marquee) == "Bo, Cy, Ada"));
        await Task.Delay(Settle * 2);

        Assert.Equal("Bo, Cy, Ada", Named(Assert.Single(Sent<SetMarqueeCommand>())));
    }

    /// <summary>The other one: the break card sent twice within 2ms, both off, when a provider said
    /// its track moved. The provider's own message and break music's relay of it both redraw.</summary>
    [Fact]
    public async Task AProviderTrackChange_AfterTheScreenJoined_SendsNoCardItAlreadyHas()
    {
        var provider = Substitute.For<IBreakMusicProvider>();
        provider.SourceName.Returns("Spotify");
        provider.ReadPlaybackAsync(Arg.Any<CancellationToken>()).Returns(BreakMusicPlayback.Paused);

        var breakMusic = new BreakMusicService(NullLogger<BreakMusicService>.Instance, [provider], _venues, _broker);
        _built.Add(breakMusic);
        PlaybackOverTheScreen(breakMusic);

        // Startup's order: break music comes up before any screen can register.
        await breakMusic.InitializeAsync();
        await Task.Delay(Settle * 2);
        _screenServer.ClearReceivedCalls();

        Connect("conn-a");
        Assert.True(await WaitForSentAsync<SetBreakMusicCardCommand>());

        var relayed = 0;
        using var relay = _broker.Subscribe<BreakMusicChanged>(_ => Interlocked.Increment(ref relayed));

        _broker.Announce(new BreakMusicTrackChanged("Spotify"));

        Assert.True(await WaitUntilAsync(() => Volatile.Read(ref relayed) > 0));
        await Task.Delay(Settle * 2);

        Assert.False(Assert.Single(Sent<SetBreakMusicCardCommand>()).Enabled);
    }

    /// <summary>A screen that registers again holds nothing, so it is sent all of it, even what
    /// has not changed since the last screen was sent it.</summary>
    [Fact]
    public async Task AScreenThatComesBack_IsSentEveryOverlay_EvenUnchanged()
    {
        PlaybackOverTheScreen(Substitute.For<IBreakMusicService>());
        var first = Connect("conn-a");

        Assert.True(await WaitForSentAsync<SetMarqueeCommand>());
        await Task.Delay(Settle * 2);

        Disconnect(first);
        _screenServer.ClearReceivedCalls();
        Connect("conn-b");

        Assert.True(await WaitForSentAsync<SetMarqueeCommand>(marquee => Named(marquee) == "Bo, Cy"));
        Assert.True(await WaitForSentAsync<SetScreenQrCodesCommand>());
        Assert.True(await WaitForSentAsync<SetBreakMusicCardCommand>());
    }

    private static string Named(SetMarqueeCommand marquee) => string.Join(", ", marquee.Singers);

    private async Task RotateAsync()
    {
        // The real rotation reads the venue and writes the queue before it says so.
        await Task.Delay(100);
        _upNext = "Bo, Cy, Ada";
        _broker.Announce(new SingerQueueChanged());
    }

    private PlaybackService PlaybackOverTheScreen(IBreakMusicService breakMusic)
    {
        PlaybackService? playback = null;

        var services = new ServiceCollection()
            .AddSingleton(_named)
            .AddSingleton(_qrCodes)
            .AddSingleton(breakMusic)
            .AddSingleton<IPlaybackService>(_ => playback!)
            .BuildServiceProvider();

        // Real, since it is what turns the stop's several announcements into the marquee's one.
        var upNext = new UpNextService(
            NullLogger<UpNextService>.Instance, _broker, _venues, _queue, _performances,
            Substitute.For<IMediaService>(), services, Settle);
        _built.Add(upNext);

        var screen = new LocalScreenDisplayProvider(
            NullLogger<LocalScreenDisplayProvider>.Instance, _screenServer, [], _broker, _venues,
            services: services, redrawSettle: Settle);
        _built.Add(screen);

        var options = Substitute.For<IOptionsMonitor<PlaybackService.ServiceOptions>>();
        options.CurrentValue.Returns(new PlaybackService.ServiceOptions
        {
            StopFadeDuration = Fade,
            PitchSettleDelay = TimeSpan.Zero,
            StreamRetireGrace = TimeSpan.Zero,
        });

        playback = new PlaybackService(
            NullLogger<PlaybackService>.Instance,
            _queue,
            _performances,
            _venues,
            Substitute.For<IAnalyticsService>(),
            _streams,
            new MediaRendererService(
                NullLogger<MediaRendererService>.Instance, [], new StreamingMediaRenderer(_streams)),
            [screen],
            breakMusic,
            options,
            Substitute.For<IAudioTrackService>(),
            _gate,
            Substitute.For<IFlashService>(),
            _broker);
        _built.Add(playback);

        return playback;
    }

    private IScreenConnection Connect(string connectionId)
    {
        var connection = Substitute.For<IScreenConnection>();
        connection.ScreenId.Returns(LocalScreenDisplayProvider.LocalScreenId);
        connection.ConnectionId.Returns(connectionId);
        connection.IsConnected.Returns(true);

        _screenServer.ScreenConnected += Raise.EventWith(
            _screenServer, new ScreenConnectionEventArgs { Connection = connection });

        return connection;
    }

    private void Disconnect(IScreenConnection connection)
        => _screenServer.ScreenDisconnected += Raise.EventWith(
            _screenServer, new ScreenConnectionEventArgs { Connection = connection });

    private static (Performance, Media) Song()
    {
        var media = new Media { Id = Guid.NewGuid(), FilePath = "/music/africa.mp4", Title = "Africa", Status = MediaStatus.Ready };
        var performance = new Performance
        {
            Id = Guid.NewGuid(),
            SingerId = Guid.NewGuid(),
            MediaId = media.Id,
            CreatedDate = DateTime.UtcNow,
            QueuePosition = 1,
        };

        return (performance, media);
    }

    private List<TCommand> Sent<TCommand>() where TCommand : IScreenCommand
        => [.. _screenServer.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IScreenServer.BroadcastCommandAsync))
            .Select(call => call.GetArguments()[0])
            .OfType<TCommand>()];

    private Task<bool> WaitForSentAsync<TCommand>(Func<TCommand, bool>? matches = null)
        where TCommand : IScreenCommand
        => WaitUntilAsync(() => Sent<TCommand>().Any(command => matches?.Invoke(command) ?? true));

    private static async Task<bool> WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 500; attempt++)
        {
            if (condition()) return true;
            await Task.Delay(10);
        }

        return false;
    }
}
