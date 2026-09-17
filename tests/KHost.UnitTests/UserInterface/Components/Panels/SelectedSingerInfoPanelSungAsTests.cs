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

/// <summary>
/// A guest picking from their phone types a name per song, so a turn on somebody's list can belong
/// to a name that is not theirs. Nothing on the row said so, and the host reads the row out.
/// </summary>
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

    /// <summary>
    /// Aliases on, because that is the venue these tests are about. A venue that has not asked for
    /// them shows neither the line nor the action, which the two tests at the end cover.
    /// </summary>
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

        // The name itself, not just that there is one — the host reads this off the row.
        Assert.Equal("singing as flo", panel.Find(SungAsSelector).TextContent.Trim());
    }

    /// <summary>
    /// The one that matters. Every enqueue records a name, filling in the singer's own when the
    /// caller has none, so a mark keyed on the column being set would appear on nearly every row
    /// and mean nothing.
    /// </summary>
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

    /// <summary>
    /// A venue that announces everyone by their own name has no use for the recorded one, so the
    /// line would say something the room will never hear. Still recorded, just not shown.
    /// </summary>
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

    /// <summary>Nothing to change when the room would not hear it — the action goes with the line.</summary>
    [Fact]
    public void TheAliasAction_IsHidden_WhenTheVenueDoesNotAllowAliases()
    {
        _venue.Settings.AllowAliases = false;
        _performance.SungAs = "flo";

        Assert.DoesNotContain(AliasActionLabel, Render<SelectedSingerInfoPanel>().Markup);
    }
}
