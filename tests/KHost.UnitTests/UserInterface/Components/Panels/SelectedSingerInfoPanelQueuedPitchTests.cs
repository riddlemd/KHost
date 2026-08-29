using Bunit;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services.Messaging;
using KHost.Plugins.Sdk.Messaging;
using KHost.UserInterface.Components.Panels;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.UserInterface.Components.Panels;

/// <summary>
/// A song re-queued from history arrives already transposed. Without a mark on the row the host
/// only finds that out when it starts, which is the wrong moment to be surprised by a key change.
/// </summary>
public class SelectedSingerInfoPanelQueuedPitchTests : BunitContext
{
    private const string PitchSelector = ".kh-selected-singer-info-panel__pitch";

    private readonly ISingerQueueService _queue = Substitute.For<ISingerQueueService>();
    private readonly IPerformanceService _performances = Substitute.For<IPerformanceService>();
    private readonly IMediaService _mediaService = Substitute.For<IMediaService>();
    private readonly KHostUser _singer = new() { Id = Guid.NewGuid(), Name = "Ann" };
    private readonly Performance _performance;
    private readonly Media _media;

    public SelectedSingerInfoPanelQueuedPitchTests()
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
        venues.ReadSelectedVenueAsync().Returns(new Venue { Id = Guid.NewGuid(), Name = "Bar" });

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

    [Theory]
    [InlineData(-2, "−2")]
    [InlineData(3, "+3")]
    public void QueuedRow_MarksASongThatWillPlayTransposed(int semitones, string expected)
    {
        _performance.Pitch = semitones;

        var panel = Render<SelectedSingerInfoPanel>();

        Assert.Equal(expected, panel.Find(PitchSelector).TextContent.Trim());
    }

    [Theory]
    [InlineData(-20, "\u221220%")]
    [InlineData(15, "+15%")]
    public void QueuedRow_MarksASongThatWillPlayAtAnotherSpeed(int tempo, string expected)
    {
        _performance.Tempo = tempo;

        var panel = Render<SelectedSingerInfoPanel>();

        Assert.Equal(expected, panel.Find(".kh-selected-singer-info-panel__tempo").TextContent.Trim());
    }

    [Fact]
    public void QueuedRow_MarksKeyAndSpeedSeparately_WhenBothAreSet()
    {
        _performance.Pitch = 2;
        _performance.Tempo = -10;

        var panel = Render<SelectedSingerInfoPanel>();

        // Two changes, two marks: one merged badge hides which of them the host actually made.
        Assert.Equal("+2", panel.Find(PitchSelector).TextContent.Trim());
        Assert.Equal("\u221210%", panel.Find(".kh-selected-singer-info-panel__tempo").TextContent.Trim());
    }

    [Fact]
    public void QueuedRow_IsNotMarked_ForASongAtTheRecordedSpeed()
    {
        var panel = Render<SelectedSingerInfoPanel>();

        Assert.Empty(panel.FindAll(".kh-selected-singer-info-panel__tempo"));
    }

    [Fact]
    public void QueuedRow_IsNotMarked_ForASongInTheWrittenKey()
    {
        var panel = Render<SelectedSingerInfoPanel>();

        // Most rows are zero; marking them all would hide the few that are not.
        Assert.Empty(panel.FindAll(PitchSelector));
    }
}
