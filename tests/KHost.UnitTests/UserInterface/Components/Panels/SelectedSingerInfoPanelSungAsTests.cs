using Bunit;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services.Messaging;
using KHost.Abstractions.Messaging;
using KHost.UserInterface.Components.Panels;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.UserInterface.Components.Panels;

/// <summary>Guards the "singing as" mark: a turn can belong to a name that is not the singer's.</summary>
public class SelectedSingerInfoPanelSungAsTests : BunitContext
{
    private const string SungAsSelector = ".kh-selected-singer-info-panel__sung-as";
    private const string AliasActionLabel = "Change Singer Alias";

    private readonly ISingerQueueService _queue = Substitute.For<ISingerQueueService>();
    private readonly IPerformanceService _performances = Substitute.For<IPerformanceService>();
    private readonly IMediaService _mediaService = Substitute.For<IMediaService>();
    private readonly KHostUser _singer = new() { Id = Guid.NewGuid(), Name = "Ann" };
    private readonly Performance _performance;
    private readonly Media _media;

    /// <summary>Aliases on; an opted-out venue shows neither the line nor the action.</summary>
    private readonly Venue _venue = new()
    {
        Id = Guid.NewGuid(),
        Name = "Bar",
        Settings = new() { AllowAliases = true },
    };

    public SelectedSingerInfoPanelSungAsTests()
    {
        _performance = new Performance { Id = Guid.NewGuid(), SingerId = _singer.Id, MediaId = Guid.NewGuid() };
        _media = new Media { Id = _performance.MediaId, FilePath = "/music/song.mp4", Title = "Song", Status = MediaStatus.Ready };

        _queue.SelectedUser.Returns(_singer);
        _queue.SelectedUserId.Returns(_singer.Id);
        _performances.ReadQueuedAsync().Returns(_ => [_performance]);
        _mediaService.ReadAsync(_media.Id).Returns(_ => _media);

        JSInterop.Mode = JSRuntimeMode.Loose;

        var permissions = Substitute.For<IPermissionService>();
        permissions.HasAsync(Arg.Any<KHostPermission>()).Returns(true);

        var venues = Substitute.For<IVenuesService>();
        venues.ReadSelectedVenueAsync().Returns(_ => _venue);

        Services.AddSingleton(_queue);
        Services.AddSingleton(_performances);
        Services.AddSingleton(permissions);
        Services.AddSingleton(venues);
        Services.AddSingleton(_mediaService);
        Services.AddSingleton(Substitute.For<IPlaybackService>());
        Services.AddSingleton(Substitute.For<IMediaSearchService>());
        Services.AddSingleton(Substitute.For<IPreparedMediaService>());
        Services.AddSingleton(Substitute.For<IUsersService>());
        Services.AddSingleton(Substitute.For<IUserGroupsService>());
        Services.AddSingleton(Substitute.For<IDialogService>());
        Services.AddSingleton(Substitute.For<ITipsService>());
        Services.AddSingleton<IMessageBroker>(new MessageBroker(NullLogger<MessageBroker>.Instance));
    }

    [Fact]
    public void QueuedRow_NamesTheNameATurnWasQueuedUnder()
    {
        _performance.SungAs = "flo";

        var panel = Render<SelectedSingerInfoPanel>();

        // The name itself, not just that there is one, because the host reads this off the row.
        Assert.Equal("singing as flo", panel.Find(SungAsSelector).TextContent.Trim());
    }

    /// <summary>Every enqueue records a name; keying the mark on it would flag nearly every row.</summary>
    [Fact]
    public void QueuedRow_IsNotMarked_WhenTheTurnCarriesTheSingersOwnName()
    {
        _performance.SungAs = _singer.Name;

        Assert.Empty(Render<SelectedSingerInfoPanel>().FindAll(SungAsSelector));
    }

    [Fact]
    public void QueuedRow_IsNotMarked_WhenTheNameDiffersOnlyInCase()
    {
        // Typing your own name back in lower case is not renaming yourself.
        _performance.SungAs = "ann";

        Assert.Empty(Render<SelectedSingerInfoPanel>().FindAll(SungAsSelector));
    }

    [Fact]
    public void QueuedRow_IsNotMarked_WhenTheNameDiffersOnlyInSurroundingSpace()
    {
        _performance.SungAs = "  Ann ";

        Assert.Empty(Render<SelectedSingerInfoPanel>().FindAll(SungAsSelector));
    }

    [Fact]
    public void QueuedRow_IsNotMarked_WhenNoNameWasRecorded()
    {
        // A row from before the column existed, and one whose singer has since been renamed.
        _performance.SungAs = null;

        Assert.Empty(Render<SelectedSingerInfoPanel>().FindAll(SungAsSelector));
    }

    /// <summary>Naming everyone by their own name shows a line the room never hears.</summary>
    [Fact]
    public void QueuedRow_IsNotMarked_WhenTheVenueDoesNotAllowAliases()
    {
        _venue.Settings.AllowAliases = false;
        _performance.SungAs = "flo";

        Assert.Empty(Render<SelectedSingerInfoPanel>().FindAll(SungAsSelector));
    }

    [Fact]
    public void TheAliasAction_IsOffered_WhenTheVenueAllowsAliases()
    {
        _performance.SungAs = "flo";

        Assert.Contains(AliasActionLabel, Render<SelectedSingerInfoPanel>().Markup);
    }

    /// <summary>Nothing to change when the room would not hear it; the action follows the line.</summary>
    [Fact]
    public void TheAliasAction_IsHidden_WhenTheVenueDoesNotAllowAliases()
    {
        _venue.Settings.AllowAliases = false;
        _performance.SungAs = "flo";

        Assert.DoesNotContain(AliasActionLabel, Render<SelectedSingerInfoPanel>().Markup);
    }
}
