using KHost.Domain.Services.Messaging;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Abstractions.Services.IPC;
using KHost.Domain.Services.BreakMusic;
using KHost.Abstractions.Messaging.Messages;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.Domain.Services.BreakMusic;

public class LibraryBreakMusicProviderTests : IDisposable
{
    private const string AudioScreenId = "screen-1";

    private readonly IMediaPoolService _pools = Substitute.For<IMediaPoolService>();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);
    private readonly IMediaService _media = Substitute.For<IMediaService>();
    private readonly IMediaStreamService _streams = Substitute.For<IMediaStreamService>();
    private readonly IScreenServer _screenServer = Substitute.For<IScreenServer>();
    private readonly IDisplayProvider _display = Substitute.For<IDisplayProvider>();
    private readonly IVenuesService _venues = Substitute.For<IVenuesService>();
    private readonly List<IScreenCommand> _sent = [];
    private readonly LibraryBreakMusicProvider _provider;

    private readonly Guid _poolId = Guid.NewGuid();
    private readonly Guid _venueId = Guid.NewGuid();

    public LibraryBreakMusicProviderTests()
    {
        // The bed goes to whatever the song is coming out of now, so the display is what records
        // it. Rebuilt into commands rather than asserted per method, so the assertions below still
        // read as "what did the room get", which is the question they were always asking.
        _display.Name.Returns("Test display");
        _display.ConnectedDeviceId.Returns(AudioScreenId);
        _display.LoadBackgroundAsync(Arg.Do<LoadBackgroundCommand>(_sent.Add), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _display.PlayBackgroundAsync(Arg.Any<CancellationToken>())
            .Returns(_ => { _sent.Add(new PlayBackgroundCommand()); return Task.CompletedTask; });
        _display.PauseBackgroundAsync(Arg.Any<CancellationToken>())
            .Returns(_ => { _sent.Add(new PauseBackgroundCommand()); return Task.CompletedTask; });
        _display.StopBackgroundAsync(Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(call => { _sent.Add(new StopBackgroundCommand { FadeDuration = call.ArgAt<TimeSpan?>(0) }); return Task.CompletedTask; });

        _streams.OpenAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<AudioMix?>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new MediaStreamSession
            {
                Id = "bed-stream",
                SourcePath = call.ArgAt<string>(0),
                PlaylistUrl = "http://host/media/bed-stream/stream.m3u8",
                StartOffset = TimeSpan.Zero,
                Pitch = 0,
                Tempo = 0,
            }));

        _provider = new LibraryBreakMusicProvider(
            NullLogger<LibraryBreakMusicProvider>.Instance,
            _pools, _media, _streams, _screenServer, [_display], _venues, _broker);
    }

    public void Dispose()
    {
        _provider.Dispose();
        GC.SuppressFinalize(this);
    }

    private void VenueWithPool(Guid? poolId)
        => _venues.ReadSelectedVenueAsync().Returns(Task.FromResult<Venue?>(new Venue
        {
            Id = _venueId,
            Name = "The Bar",
            Settings = new Venue.VenueSettings { BreakMusicPoolId = poolId },
        }));

    private Media PoolYields(string title = "Bed Track", MediaStatus status = MediaStatus.Ready)
    {
        var media = new Media
        {
            Id = Guid.NewGuid(),
            FilePath = "/media/bed.mp3",
            Title = title,
            Artist = "Someone",
            Status = status,
            Type = MediaType.Audio,
        };

        _pools.SelectNextAsync(_poolId, Arg.Any<Guid?>())
            .Returns(Task.FromResult<MediaPoolEntry?>(new MediaPoolEntry { MediaId = media.Id }));
        _media.ReadAsync(media.Id).Returns(Task.FromResult<Media?>(media));

        return media;
    }

    [Fact]
    public async Task StartAsync_WithNoPoolChosen_DoesNotPlay()
    {
        VenueWithPool(null);

        Assert.False(await _provider.StartAsync());
        Assert.Empty(_sent);
    }

    [Fact]
    public async Task StartAsync_WhenThePoolIsEmpty_DoesNotPlay()
    {
        VenueWithPool(_poolId);
        _pools.SelectNextAsync(_poolId, Arg.Any<Guid?>()).Returns(Task.FromResult<MediaPoolEntry?>(null));

        Assert.False(await _provider.StartAsync());
        Assert.Empty(_sent);
    }

    [Fact]
    public async Task StartAsync_LoadsTheTrackOnTheBackgroundChannel()
    {
        VenueWithPool(_poolId);
        PoolYields();

        Assert.True(await _provider.StartAsync());

        var load = Assert.IsType<LoadBackgroundCommand>(_sent[0]);
        Assert.Equal("http://host/media/bed-stream/stream.m3u8", load.StreamUrl);
    }

    // The console redraws off this message alone since the provider raises no event; a provider
    // that plays without announcing leaves the panel showing the previous track forever.
    [Fact]
    public async Task StartAsync_AnnouncesTheTrackUnderItsOwnSourceName()
    {
        VenueWithPool(_poolId);
        PoolYields();

        var announced = new List<string>();
        using var subscription = _broker.Subscribe<BreakMusicTrackChanged>(m => announced.Add(m.ProviderSourceName));

        Assert.True(await _provider.StartAsync());

        Assert.Equal([_provider.SourceName], announced);
    }

    [Fact]
    public async Task StartAsync_SendsToTheDisplayRatherThanBroadcasting()
    {
        VenueWithPool(_poolId);
        PoolYields();

        await _provider.StartAsync();

        await _display.Received().LoadBackgroundAsync(Arg.Any<LoadBackgroundCommand>(), Arg.Any<CancellationToken>());
        await _screenServer.DidNotReceive().BroadcastCommandAsync(Arg.Any<IScreenCommand>());
    }

    [Fact]
    public async Task StartAsync_WithNothingConnected_DoesNotPlayAndClosesTheStream()
    {
        VenueWithPool(_poolId);
        PoolYields();
        _display.ConnectedDeviceId.Returns((string?)null);

        Assert.False(await _provider.StartAsync());

        // The transcode opened before the send was refused; leaving it running burns CPU on a
        // track nobody can hear.
        await _streams.Received(1).CloseAsync("bed-stream");
    }

    [Fact]
    public async Task StartAsync_SkipsMediaThatIsNotReady()
    {
        VenueWithPool(_poolId);
        PoolYields(status: MediaStatus.Broken);

        Assert.False(await _provider.StartAsync());
        Assert.Empty(_sent);
    }

    [Fact]
    public async Task StartAsync_ReportsTheTrackItPlayed()
    {
        VenueWithPool(_poolId);
        PoolYields("Elevator Jazz");

        await _provider.StartAsync();

        Assert.Equal("Elevator Jazz", _provider.CurrentTrack?.Title);
    }

    [Fact]
    public async Task SkipAsync_ClosesTheOldStreamBeforeOpeningTheNext()
    {
        VenueWithPool(_poolId);
        PoolYields();
        await _provider.StartAsync();

        await _provider.SkipAsync();

        await _streams.Received(1).CloseAsync("bed-stream");
    }

    [Fact]
    public async Task StopAsync_ClearsTheTrackAndClosesTheStream()
    {
        VenueWithPool(_poolId);
        PoolYields();
        await _provider.StartAsync();

        await _provider.StopAsync();

        Assert.Null(_provider.CurrentTrack);
        await _streams.Received(1).CloseAsync("bed-stream");
        Assert.Contains(_sent, c => c is StopBackgroundCommand);
    }

    // This provider's audio rides the display's second channel, whose level the display sets from
    // the venue alongside the song's. Setting it here too would be a second place for one number.
    [Fact]
    public async Task SetVolumeAsync_SendsNothing()
    {
        await _provider.SetVolumeAsync(0.3f);

        Assert.Empty(_sent);
    }

    [Fact]
    public async Task PlayingATrack_DoesNotSetTheChannelVolume()
    {
        VenueWithPool(_poolId);
        PoolYields();

        await _provider.StartAsync();

        Assert.DoesNotContain(_sent, c => c is SetBackgroundVolumeCommand);
    }

    [Fact]
    public void RendersThroughHost_IsTrue()
    {
        // The library provider's audio rides the screen, which is what lets it reach a Cast device
        // and what makes a connected screen a requirement.
        Assert.True(_provider.RendersThroughHost);
    }

    [Fact]
    public async Task PauseAsync_SendsPauseOnTheBackgroundChannelOnly()
    {
        await _provider.PauseAsync();

        Assert.IsType<PauseBackgroundCommand>(Assert.Single(_sent));
    }
}
