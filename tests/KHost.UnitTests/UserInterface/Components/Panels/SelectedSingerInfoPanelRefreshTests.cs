using Bunit;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Common.Monetary;
using KHost.Domain.Services.Messaging;
using KHost.UserInterface.Components.Panels;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.UserInterface.Components.Panels;

/// <summary>One song load announces the queue, the performances and playback together; each
/// message re-reads only what it moved, and overlapping reads never let an older answer land last.</summary>
public class SelectedSingerInfoPanelRefreshTests : BunitContext
{
    private const string RowSelector = ".kh-selected-singer-info-panel__row";
    private const string TipsSelector = ".kh-selected-singer-info-panel__tip-totals";

    private readonly ISingerQueueService _queue = Substitute.For<ISingerQueueService>();
    private readonly IPerformanceService _performances = Substitute.For<IPerformanceService>();
    private readonly IMediaService _mediaService = Substitute.For<IMediaService>();
    private readonly IVenuesService _venues = Substitute.For<IVenuesService>();
    private readonly ITipsService _tips = Substitute.For<ITipsService>();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);
    private readonly KHostUser _singer = new() { Id = Guid.NewGuid(), Name = "Ann" };
    private readonly Performance _first;
    private readonly Performance _second;
    private readonly Venue _venue = new() { Id = Guid.NewGuid(), Name = "Bar" };

    public SelectedSingerInfoPanelRefreshTests()
    {
        _first = new Performance { Id = Guid.NewGuid(), SingerId = _singer.Id, MediaId = Guid.NewGuid() };
        _second = new Performance { Id = Guid.NewGuid(), SingerId = _singer.Id, MediaId = Guid.NewGuid() };

        _queue.SelectedUser.Returns(_singer);
        _queue.SelectedUserId.Returns(_singer.Id);
        _performances.ReadQueuedAsync().Returns(_ => [_first]);
        _mediaService.ReadAsync(Arg.Any<Guid>())
            .Returns(args => new Media { Id = (Guid)args[0], FilePath = "/music/song.mp4", Title = "Song", Status = MediaStatus.Ready });
        _venue.Settings.TippingEnabled = true;
        _venues.ReadSelectedVenueAsync().Returns(_ => _venue);
        _tips.GetByUserIdAsync(_singer.Id).Returns(_ => []);

        JSInterop.Mode = JSRuntimeMode.Loose;

        Services.AddSingleton(_queue);
        Services.AddSingleton(_performances);
        Services.AddSingleton(_venues);
        Services.AddSingleton(_mediaService);
        Services.AddSingleton(Substitute.For<IPlaybackService>());
        Services.AddSingleton(Substitute.For<IMediaSearchService>());
        Services.AddSingleton(Substitute.For<IUsersService>());
        Services.AddSingleton(Substitute.For<IUserGroupsService>());
        Services.AddSingleton(Substitute.For<IDialogService>());
        Services.AddSingleton(_tips);
        Services.AddSingleton<IMessageBroker>(_broker);
    }

    [Fact]
    public async Task ASongLoadsBurst_ReadsTheQueueTwiceNotThrice_AndEndsOnTheNewestRows()
    {
        var panel = Render<SelectedSingerInfoPanel>();
        panel.WaitForAssertion(() => Assert.Single(panel.FindAll(RowSelector)));

        var stale = new TaskCompletionSource<List<Performance>>();
        var fresh = new TaskCompletionSource<List<Performance>>();
        var third = new TaskCompletionSource<List<Performance>>();
        _performances.ClearReceivedCalls();
        _performances.ReadQueuedAsync().Returns(stale.Task, fresh.Task, third.Task);

        await _broker.PublishAsync(new SingerQueueChanged());
        await _broker.PublishAsync(new PerformancesChanged());
        await _broker.PublishAsync(new PlaybackChanged());

        // The later reads answer first with the queue as it now is; the first answers last with what it was.
        await panel.InvokeAsync(() =>
        {
            fresh.SetResult([_first, _second]);
            third.SetResult([_first, _second]);
        });
        await panel.InvokeAsync(() => stale.SetResult([_first]));

        panel.WaitForAssertion(() => Assert.Equal(2, panel.FindAll(RowSelector).Count));
        Assert.Equal(2, QueueReads());
    }

    [Fact]
    public async Task PlaybackChanged_RereadsNothing()
    {
        Render<SelectedSingerInfoPanel>();
        ClearAllCalls();

        await _broker.PublishAsync(new PlaybackChanged());

        await _performances.DidNotReceive().ReadQueuedAsync();
        await _venues.DidNotReceive().ReadSelectedVenueAsync();
        await _mediaService.DidNotReceive().ReadAsync(Arg.Any<Guid>());
        await _tips.DidNotReceive().GetByUserIdAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task MediaLibraryChanged_RereadsTheSongs_ButNotTheQueue()
    {
        Render<SelectedSingerInfoPanel>();
        ClearAllCalls();

        await _broker.PublishAsync(new MediaLibraryChanged());

        await _mediaService.Received(1).ReadAsync(_first.MediaId);
        await _performances.DidNotReceive().ReadQueuedAsync();
        await _venues.DidNotReceive().ReadSelectedVenueAsync();
    }

    [Fact]
    public async Task SelectedVenueChanged_TurningTippingOff_HidesTheTotals_WithoutRereadingTheQueue()
    {
        var panel = Render<SelectedSingerInfoPanel>();
        Assert.Single(panel.FindAll(TipsSelector));
        ClearAllCalls();

        _venue.Settings.TippingEnabled = false;
        await _broker.PublishAsync(new SelectedVenueChanged());

        panel.WaitForAssertion(() => Assert.Empty(panel.FindAll(TipsSelector)));
        await _performances.DidNotReceive().ReadQueuedAsync();
    }

    [Fact]
    public async Task SelectedVenueChanged_TurningTippingOn_ReadsTheTotals()
    {
        _venue.Settings.TippingEnabled = false;
        _tips.GetByUserIdAsync(_singer.Id).Returns([new Tip { UserId = _singer.Id, AmountInCents = 500, CreatedDate = DateTime.UtcNow.AddYears(-1) }]);
        var panel = Render<SelectedSingerInfoPanel>();
        Assert.Empty(panel.FindAll(TipsSelector));

        _venue.Settings.TippingEnabled = true;
        await _broker.PublishAsync(new SelectedVenueChanged());

        panel.WaitForAssertion(() => Assert.Contains(500.CentsToCurrencyString(), panel.Find(TipsSelector).TextContent));
    }

    [Fact]
    public async Task TipsChanged_ShowsTheNewTotal_WithoutRereadingTheQueueOrVenue()
    {
        var panel = Render<SelectedSingerInfoPanel>();
        ClearAllCalls();

        _tips.GetByUserIdAsync(_singer.Id).Returns([new Tip { UserId = _singer.Id, AmountInCents = 500, CreatedDate = DateTime.UtcNow.AddYears(-1) }]);
        await _broker.PublishAsync(new TipsChanged());

        panel.WaitForAssertion(() => Assert.Contains(500.CentsToCurrencyString(), panel.Find(TipsSelector).TextContent));
        await _performances.DidNotReceive().ReadQueuedAsync();
        await _venues.DidNotReceive().ReadSelectedVenueAsync();
    }

    [Fact]
    public async Task SingerQueueChanged_AnotherSingerSelected_ReadsTheirTotals()
    {
        var other = new KHostUser { Id = Guid.NewGuid(), Name = "Bo" };
        _tips.GetByUserIdAsync(other.Id).Returns([new Tip { UserId = other.Id, AmountInCents = 700, CreatedDate = DateTime.UtcNow.AddYears(-1) }]);
        var panel = Render<SelectedSingerInfoPanel>();

        _queue.SelectedUser.Returns(other);
        _queue.SelectedUserId.Returns(other.Id);
        await _broker.PublishAsync(new SingerQueueChanged());

        panel.WaitForAssertion(() => Assert.Contains(700.CentsToCurrencyString(), panel.Find(TipsSelector).TextContent));
    }

    private int QueueReads()
        => _performances.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(IPerformanceService.ReadQueuedAsync));

    private void ClearAllCalls()
    {
        _performances.ClearReceivedCalls();
        _venues.ClearReceivedCalls();
        _mediaService.ClearReceivedCalls();
        _tips.ClearReceivedCalls();
    }
}
