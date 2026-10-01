using Bunit;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services.Messaging;
using KHost.UserInterface.Components.Panels;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.UserInterface.Components.Panels;

/// <summary>Each waiting turn's row menu opens its key, tempo and levels; the turn at the microphone
/// is the song controls' to change instead.</summary>
public class SelectedSingerInfoPanelSettingsTests : BunitContext
{
    private const string SettingsAction = ".kh-selected-singer-info-panel__settings-btn";

    private readonly ISingerQueueService _queue = Substitute.For<ISingerQueueService>();
    private readonly IPerformanceService _performances = Substitute.For<IPerformanceService>();
    private readonly IMediaService _mediaService = Substitute.For<IMediaService>();
    private readonly IPlaybackService _playback = Substitute.For<IPlaybackService>();
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();

    private readonly KHostUser _singer = new() { Id = Guid.NewGuid(), Name = "Ann" };
    private readonly Performance _first;
    private readonly Performance _second;

    public SelectedSingerInfoPanelSettingsTests()
    {
        _first = new Performance { Id = Guid.NewGuid(), SingerId = _singer.Id, MediaId = Guid.NewGuid(), Pitch = 1 };
        _second = new Performance { Id = Guid.NewGuid(), SingerId = _singer.Id, MediaId = Guid.NewGuid() };

        _queue.SelectedUser.Returns(_singer);
        _queue.SelectedUserId.Returns(_singer.Id);
        _performances.ReadQueuedAsync().Returns(_ => [_first, _second]);

        _mediaService.ReadAsync(Arg.Any<Guid>()).Returns(call => new Media
        {
            Id = call.Arg<Guid>(),
            FilePath = $"/music/{call.Arg<Guid>()}.mp4",
            Title = "Song",
            Status = MediaStatus.Ready,
        });

        JSInterop.Mode = JSRuntimeMode.Loose;

        var permissions = Substitute.For<IPermissionService>();
        permissions.HasAsync(Arg.Any<KHostPermission>()).Returns(true);

        Services.AddSingleton(_queue);
        Services.AddSingleton(_performances);
        Services.AddSingleton(_mediaService);
        Services.AddSingleton(_playback);
        Services.AddSingleton(permissions);
        // Tipping off: an unstubbed tips read is a null list the panel would sum.
        var venues = Substitute.For<IVenuesService>();
        venues.ReadSelectedVenueAsync().Returns(new Venue { Name = "Bar", Settings = new() { TippingEnabled = false } });
        Services.AddSingleton(venues);
        Services.AddSingleton(Substitute.For<IMediaSearchService>());
        Services.AddSingleton(Substitute.For<IUsersService>());
        Services.AddSingleton(Substitute.For<IUserGroupsService>());
        Services.AddSingleton(_dialogs);
        Services.AddSingleton(Substitute.For<ITipsService>());
        Services.AddSingleton<IMessageBroker>(new MessageBroker(NullLogger<MessageBroker>.Instance));
    }

    [Fact]
    public void EveryQueuedRow_OffersTheAction()
    {
        var actions = Render<SelectedSingerInfoPanel>().FindAll(SettingsAction);

        Assert.Equal(2, actions.Count);
        Assert.All(actions, action => Assert.False(action.HasAttribute("disabled")));
    }

    [Fact]
    public void Clicking_OpensTheEditorOnThatTurnAndItsSong()
    {
        Render<SelectedSingerInfoPanel>().FindAll(SettingsAction)[1].Click();

        _dialogs.Received(1).RequestPerformanceSettingsAsync(
            _second,
            Arg.Is<Media?>(m => m != null && m.Id == _second.MediaId),
            Arg.Any<Func<PerformanceSettings, Task>>(),
            Arg.Any<Action?>(),
            Arg.Any<Action?>());
    }

    /// <summary>The save goes through the service that owns turns, against the turn clicked.</summary>
    [Fact]
    public async Task SavingTheEditor_WritesThroughThePerformanceService()
    {
        Func<PerformanceSettings, Task>? onSave = null;
        _dialogs.RequestPerformanceSettingsAsync(
                Arg.Any<Performance>(), Arg.Any<Media?>(), Arg.Do<Func<PerformanceSettings, Task>>(f => onSave = f),
                Arg.Any<Action?>(), Arg.Any<Action?>())
            .Returns(Task.CompletedTask);

        Render<SelectedSingerInfoPanel>().FindAll(SettingsAction)[0].Click();
        var settings = new PerformanceSettings(-3, 10, 20, null);
        await onSave!(settings);

        await _performances.Received(1).UpdateSettingsAsync(_first.Id, settings);
    }

    [Fact]
    public void TheLoadedTurn_HasTheActionDisabledWithTheReason()
    {
        _playback.CurrentPerformance.Returns(_first);

        var actions = Render<SelectedSingerInfoPanel>().FindAll(SettingsAction);

        Assert.True(actions[0].HasAttribute("disabled"));
        Assert.Contains("song controls", actions[0].GetAttribute("title"), StringComparison.OrdinalIgnoreCase);
        Assert.False(actions[1].HasAttribute("disabled"));
        Assert.DoesNotContain("song controls", actions[1].GetAttribute("title"), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The button's disabled state can be a render behind a load; the handler re-checks.</summary>
    [Fact]
    public void TheLoadedTurn_ClickedAnyway_OpensNothing()
    {
        var actions = Render<SelectedSingerInfoPanel>().FindAll(SettingsAction);
        _playback.CurrentPerformance.Returns(_first);

        actions[0].Click();

        _dialogs.DidNotReceiveWithAnyArgs().RequestPerformanceSettingsAsync(default!, default, default!);
    }
}
