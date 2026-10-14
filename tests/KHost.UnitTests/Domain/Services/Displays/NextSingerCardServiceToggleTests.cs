using KHost.Abstractions.Messaging.Messages;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services.Displays;
using KHost.Domain.Services.Messaging;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.Domain.Services.Displays;

/// <summary>Whether the card is up: set by announcing, cleared by withdrawing or by the screen
/// drawing over it, and every change said aloud so each console redraws its button.</summary>
public class NextSingerCardServiceToggleTests : IDisposable
{
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);
    private readonly IVenuesService _venues = Substitute.For<IVenuesService>();
    private readonly ISingerQueueService _queue = Substitute.For<ISingerQueueService>();
    private readonly IPerformanceService _performances = Substitute.For<IPerformanceService>();
    private readonly IMediaService _media = Substitute.For<IMediaService>();
    private readonly IPlaybackService _playback = Substitute.For<IPlaybackService>();
    private readonly NextSingerCardService _service;

    private int _withdrawn;
    private bool? _upWhenAnnounced;
    private readonly IDisposable _withdrawnCounter;
    private readonly IDisposable _announcedProbe;

    public NextSingerCardServiceToggleTests()
    {
        _venues.ReadSelectedVenueAsync().Returns(_ => new Venue { Name = "Bar", Settings = new() });
        _queue.Users.Returns(_ => [new KHostUser { Id = Guid.NewGuid(), Name = "Ada" }]);
        _performances.ReadQueuedAsync().Returns(_ => []);
        _playback.CurrentProgram.Returns(new PlaybackProgram.Idle());

        _service = new(NullLogger<NextSingerCardService>.Instance, _broker, _venues, _queue, _performances, _media, _playback);
        _withdrawnCounter = _broker.Subscribe<NextSingerWithdrawn>(_ => _withdrawn++);
        _announcedProbe = _broker.Subscribe<NextSingerAnnounced>(_ => _upWhenAnnounced = _service.IsAnnounced);
    }

    [Fact]
    public async Task AnnounceAsync_SetsIsAnnounced_BeforeTheCardIsHandedOn()
    {
        await _service.AnnounceAsync();

        Assert.True(_service.IsAnnounced);
        Assert.True(_upWhenAnnounced);
    }

    [Fact]
    public async Task AnnounceAsync_NobodyQueued_LeavesIsAnnouncedClear()
    {
        _queue.Users.Returns(_ => []);

        Assert.False(await _service.AnnounceAsync());
        Assert.False(_service.IsAnnounced);
    }

    [Fact]
    public async Task WithdrawAsync_CardUp_PublishesTheWithdrawalAndClearsIsAnnounced()
    {
        await _service.AnnounceAsync();

        await _service.WithdrawAsync();

        Assert.False(_service.IsAnnounced);
        Assert.Equal(1, _withdrawn);
    }

    [Fact]
    public async Task WithdrawAsync_CardNotUp_PublishesNothing()
    {
        await _service.WithdrawAsync();

        Assert.Equal(0, _withdrawn);
    }

    [Fact]
    public async Task ASongStarting_ClearsIsAnnounced_AndSaysSo()
    {
        await _service.AnnounceAsync();

        _playback.CurrentProgram.Returns(new PlaybackProgram.Playing(new Media { Title = "Africa", FilePath = "/a.mp4" }, new Performance()));
        await _broker.PublishAsync(new PlaybackChanged());

        Assert.False(_service.IsAnnounced);
        Assert.Equal(1, _withdrawn);
    }

    /// <summary>PlaybackChanged is also every pause and seek, which redraw nothing on the screen.</summary>
    [Fact]
    public async Task PlaybackChangedWithTheProgramUnmoved_KeepsIsAnnounced()
    {
        var ad = new PlaybackProgram.AdStill("http://host/media/1", ImageScaling.Fit);
        _playback.CurrentProgram.Returns(ad);
        await _service.AnnounceAsync();

        _playback.CurrentProgram.Returns(ad with { });
        await _broker.PublishAsync(new PlaybackChanged());

        Assert.True(_service.IsAnnounced);
        Assert.Equal(0, _withdrawn);
    }

    [Fact]
    public async Task AVenueChangeWhileIdle_ClearsIsAnnounced_AndSaysSo()
    {
        await _service.AnnounceAsync();

        await _broker.PublishAsync(new SelectedVenueChanged());

        Assert.False(_service.IsAnnounced);
        Assert.Equal(1, _withdrawn);
    }

    /// <summary>Over an ad still the screen redraws only the venue's card, which is not what is up.</summary>
    [Fact]
    public async Task AVenueChangeOverAnAdStill_KeepsIsAnnounced()
    {
        _playback.CurrentProgram.Returns(new PlaybackProgram.AdStill("http://host/media/1", ImageScaling.Fit));
        await _service.AnnounceAsync();

        await _broker.PublishAsync(new SelectedVenueChanged());

        Assert.True(_service.IsAnnounced);
        Assert.Equal(0, _withdrawn);
    }

    [Fact]
    public async Task ProgramMovingWithNoCardUp_SaysNothing()
    {
        _playback.CurrentProgram.Returns(new PlaybackProgram.Playing(new Media { Title = "Africa", FilePath = "/a.mp4" }, new Performance()));
        await _broker.PublishAsync(new PlaybackChanged());

        Assert.Equal(0, _withdrawn);
    }

    public void Dispose()
    {
        _withdrawnCounter.Dispose();
        _announcedProbe.Dispose();
        _service.Dispose();
    }
}
