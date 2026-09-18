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

/// <summary>A turn at the microphone cannot be renamed: playback resolved the name at load, so the
/// change would reach nobody while still reading as if it had worked.</summary>
public class SelectedSingerInfoPanelAliasLockTests : BunitContext
{
    private const string AliasActionLabel = "Change Singer Alias";

    private readonly ISingerQueueService _queue = Substitute.For<ISingerQueueService>();
    private readonly IPerformanceService _performances = Substitute.For<IPerformanceService>();
    private readonly IMediaService _mediaService = Substitute.For<IMediaService>();
    private readonly IPlaybackService _playback = Substitute.For<IPlaybackService>();

    private readonly KHostUser _singer = new() { Id = Guid.NewGuid(), Name = "Ann" };
    private readonly Performance _first;
    private readonly Performance _second;

    private readonly Venue _venue = new()
    {
        Id = Guid.NewGuid(),
        Name = "Bar",
        Settings = new() { AllowAliases = true },
    };

    public SelectedSingerInfoPanelAliasLockTests()
    {
        _first = new Performance { Id = Guid.NewGuid(), SingerId = _singer.Id, MediaId = Guid.NewGuid(), SungAs = "flo" };
        _second = new Performance { Id = Guid.NewGuid(), SingerId = _singer.Id, MediaId = Guid.NewGuid(), SungAs = "flo" };

        _queue.SelectedUser.Returns(_singer);
        _queue.SelectedUserId.Returns(_singer.Id);
        _performances.ReadQueuedAsync().Returns(_ => [_first, _second]);

        _mediaService.ReadAsync(Arg.Any<Guid>()).Returns(call => new Media
        {
            Id = call.Arg<Guid>(),
            FilePath = "/music/song.mp4",
            Title = "Song",
            Status = MediaStatus.Ready,
        });

        JSInterop.Mode = JSRuntimeMode.Loose;

        var permissions = Substitute.For<IPermissionService>();
        permissions.HasAsync(Arg.Any<KHostPermission>()).Returns(true);

        var venues = Substitute.For<IVenuesService>();
        venues.ReadSelectedVenueAsync().Returns(_ => _venue);

        Services.AddSingleton(_queue);
        Services.AddSingleton(_performances);
        Services.AddSingleton(_mediaService);
        Services.AddSingleton(_playback);
        Services.AddSingleton(permissions);
        Services.AddSingleton(venues);
        Services.AddSingleton(Substitute.For<IMediaSearchService>());
        Services.AddSingleton(Substitute.For<IPreparedMediaService>());
        Services.AddSingleton(Substitute.For<IUsersService>());
        Services.AddSingleton(Substitute.For<IUserGroupsService>());
        Services.AddSingleton(Substitute.For<IDialogService>());
        Services.AddSingleton(Substitute.For<ITipsService>());
        Services.AddSingleton<IMessageBroker>(new MessageBroker(NullLogger<MessageBroker>.Instance));
    }

    [Fact]
    public void NothingLoaded_LeavesEveryAliasActionEnabled()
    {
        _playback.CurrentPerformance.Returns((Performance?)null);

        Assert.All(AliasActions(), action => Assert.False(action.HasAttribute("disabled")));
    }

    [Fact]
    public void TheSongAtTheMicrophone_HasItsAliasActionDisabled()
    {
        _playback.CurrentPerformance.Returns(_first);

        Assert.True(AliasActions()[0].HasAttribute("disabled"));
    }

    /// <summary>The scope of the whole rule: the song locks, never the singer. Somebody at the
    /// microphone on their first song still has an editable alias on the rest of their queue.
    /// </summary>
    [Fact]
    public void TheSingersOtherSongs_StayEditableWhileTheyAreSinging()
    {
        _playback.CurrentPerformance.Returns(_first);

        Assert.False(AliasActions()[1].HasAttribute("disabled"));
    }

    /// <summary>Disabled and silent reads as broken, so the reason has to travel with it.</summary>
    [Fact]
    public void TheDisabledAction_SaysWhyRatherThanRepeatingWhatItWouldDo()
    {
        _playback.CurrentPerformance.Returns(_first);

        var locked = AliasActions()[0].GetAttribute("title");

        Assert.NotNull(locked);
        Assert.Contains("microphone", locked!, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(AliasActions()[1].GetAttribute("title"), locked);
    }

    /// <summary>Hidden beats disabled when the venue does not allow aliases at all: the rule being
    /// taught there is a different one, and a greyed row would teach the wrong lesson.</summary>
    [Fact]
    public void AVenueWithoutAliases_ShowsNoActionToDisable()
    {
        _venue.Settings.AllowAliases = false;
        _playback.CurrentPerformance.Returns(_first);

        Assert.DoesNotContain(AliasActionLabel, Render<SelectedSingerInfoPanel>().Markup);
    }

    private IReadOnlyList<AngleSharp.Dom.IElement> AliasActions()
        => Render<SelectedSingerInfoPanel>()
            .FindAll("button")
            .Where(button => button.TextContent.Contains(AliasActionLabel, StringComparison.Ordinal))
            .ToList();
}
