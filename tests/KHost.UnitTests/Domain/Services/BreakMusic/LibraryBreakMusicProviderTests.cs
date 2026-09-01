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
    private readonly IScreenCoordinationService _screenCoordination = Substitute.For<IScreenCoordinationService>();
    private readonly IVenuesService _venues = Substitute.For<IVenuesService>();
    private readonly List<IScreenCommand> _sent = [];
    private readonly LibraryBreakMusicProvider _provider;

    private readonly Guid _poolId = Guid.NewGuid();
    private readonly Guid _venueId = Guid.NewGuid();

    public LibraryBreakMusicProviderTests()
    {
        _screenCoordination.EnsureRolesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<string?>(AudioScreenId));
        _screenServer.SendCommandAsync(Arg.Any<string>(), Arg.Do<IScreenCommand>(_sent.Add)).Returns(Task.CompletedTask);

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
            _pools, _media, _streams, _screenServer, _screenCoordination, _venues, _broker);
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

    // Only the screen the room hears: the bed carries no timeline, so a second screen playing it
    // would be a second bed in the room rather than the same one.
    // The console redraws off this message alone now that the provider raises no event, so a
    // provider that plays without announcing leaves the panel showing the previous track forever.
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
    public async Task StartAsync_SendsToTheAudioScreenRatherThanBroadcasting()
    {
        VenueWithPool(_poolId);
        PoolYields();

        await _provider.StartAsync();

        await _screenServer.Received().SendCommandAsync(AudioScreenId, Arg.Any<IScreenCommand>());
        await _screenServer.DidNotReceive().BroadcastCommandAsync(Arg.Any<IScreenCommand>());
    }

    [Fact]
    public async Task StartAsync_WithNoAudioScreen_DoesNotPlayAndClosesTheStream()
    {
        VenueWithPool(_poolId);
        PoolYields();
        _screenCoordination.EnsureRolesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<string?>(null));

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

    // This provider's audio rides the screen, and ScreenCoordination sets that channel from the
    // venue alongside the song's. Setting it here too would be a second place for one number.
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
