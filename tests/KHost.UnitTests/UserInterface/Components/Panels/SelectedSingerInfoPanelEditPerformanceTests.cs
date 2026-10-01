using Bunit;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services.Messaging;
using KHost.UserInterface.Components.Panels;
using KHost.UserInterface.Models;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.UserInterface.Components.Panels;

/// <summary>Each queued row's menu has one Edit Performance action for the turn's name, key, tempo
/// and levels, in place of separate alias and song-control actions.</summary>
public class SelectedSingerInfoPanelEditPerformanceTests : BunitContext
{
    private const string EditAction = ".kh-selected-singer-info-panel__edit-performance-btn";

    private readonly ISingerQueueService _queue = Substitute.For<ISingerQueueService>();
    private readonly IPerformanceService _performances = Substitute.For<IPerformanceService>();
    private readonly IMediaService _mediaService = Substitute.For<IMediaService>();
    private readonly IPlaybackService _playback = Substitute.For<IPlaybackService>();
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();

    private readonly KHostUser _singer = new() { Id = Guid.NewGuid(), Name = "Ann" };
    private readonly Performance _first;
    private readonly Performance _second;

    private readonly Venue _venue = new()
    {
        Name = "Bar",
        Settings = new() { TippingEnabled = false, AllowAliases = true },
    };

    public SelectedSingerInfoPanelEditPerformanceTests()
    {
        _first = new Performance { Id = Guid.NewGuid(), SingerId = _singer.Id, MediaId = Guid.NewGuid(), Pitch = 1, SungAs = "flo" };
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

        // Tipping off: an unstubbed tips read is a null list the panel would sum.
        var venues = Substitute.For<IVenuesService>();
        venues.ReadSelectedVenueAsync().Returns(_ => _venue);

        Services.AddSingleton(_queue);
        Services.AddSingleton(_performances);
        Services.AddSingleton(_mediaService);
        Services.AddSingleton(_playback);
        Services.AddSingleton(permissions);
        Services.AddSingleton(venues);
        Services.AddSingleton(Substitute.For<IMediaSearchService>());
        Services.AddSingleton(Substitute.For<IUsersService>());
        Services.AddSingleton(Substitute.For<IUserGroupsService>());
        Services.AddSingleton(_dialogs);
        Services.AddSingleton(Substitute.For<ITipsService>());
        Services.AddSingleton<IMessageBroker>(new MessageBroker(NullLogger<MessageBroker>.Instance));
    }

    [Fact]
    public void EveryQueuedRow_OffersEditPerformance_AndNoSeparateAliasOrControlsAction()
    {
        var panel = Render<SelectedSingerInfoPanel>();

        var actions = panel.FindAll(EditAction);
        Assert.Equal(2, actions.Count);
        Assert.All(actions, action => Assert.Contains("Edit Performance", action.TextContent));
        Assert.DoesNotContain("Change Singer Alias", panel.Markup);
        Assert.DoesNotContain("Edit Song Controls", panel.Markup);
    }

    /// <summary>Key and tempo are worth editing whatever the venue thinks of aliases.</summary>
    [Fact]
    public void AVenueWithoutAliases_StillOffersEditPerformance()
    {
        _venue.Settings.AllowAliases = false;

        Assert.Equal(2, Render<SelectedSingerInfoPanel>().FindAll(EditAction).Count);
    }

    [Fact]
    public void Clicking_OpensTheEditorOnThatTurnItsSongAndTheSingersName()
    {
        Render<SelectedSingerInfoPanel>().FindAll(EditAction)[1].Click();

        _dialogs.Received(1).RequestEditPerformanceAsync(
            _second,
            Arg.Is<Media?>(m => m != null && m.Id == _second.MediaId),
            "Ann",
            Arg.Any<Func<PerformanceEdit, Task>>(),
            Arg.Any<Action?>(),
            Arg.Any<Action?>());
    }

    /// <summary>The save goes through the service that owns turns, against the turn clicked.</summary>
    [Fact]
    public async Task SavingTheEditor_WritesThroughThePerformanceService()
    {
        Func<PerformanceEdit, Task>? onSave = null;
        _dialogs.RequestEditPerformanceAsync(
                Arg.Any<Performance>(), Arg.Any<Media?>(), Arg.Any<string?>(),
                Arg.Do<Func<PerformanceEdit, Task>>(f => onSave = f), Arg.Any<Action?>(), Arg.Any<Action?>())
            .Returns(Task.CompletedTask);
        _performances.ReadAsync(_first.Id).Returns(_ => new Performance { Id = _first.Id, SungAs = "flo" });

        Render<SelectedSingerInfoPanel>().FindAll(EditAction)[0].Click();
        var settings = new PerformanceSettings(-3, 10, 20, null);
        await onSave!(new PerformanceEdit { SungAsChanged = true, SungAs = "DJ P", Settings = settings });

        await _performances.Received(1).UpdateAsync(Arg.Is<Performance>(p => p.Id == _first.Id && p.SungAs == "DJ P"));
        await _performances.Received(1).UpdateSettingsAsync(_first.Id, settings);
    }

    /// <summary>The loaded turn still opens, read-only, so the host can see what it is set to and is
    /// told where to change it.</summary>
    [Fact]
    public void TheLoadedTurn_StillOpensWithTheReasonInItsTooltip()
    {
        _playback.CurrentPerformance.Returns(_first);

        var actions = Render<SelectedSingerInfoPanel>().FindAll(EditAction);

        Assert.False(actions[0].HasAttribute("disabled"));
        Assert.Contains("song controls", actions[0].GetAttribute("title"), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("song controls", actions[1].GetAttribute("title"), StringComparison.OrdinalIgnoreCase);

        actions[0].Click();

        _dialogs.Received(1).RequestEditPerformanceAsync(
            _first, Arg.Any<Media?>(), Arg.Any<string?>(), Arg.Any<Func<PerformanceEdit, Task>>(),
            Arg.Any<Action?>(), Arg.Any<Action?>());
    }
}
