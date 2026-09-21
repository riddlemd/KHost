using Bunit;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services.Messaging;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using KHost.UserInterface.Components.Panels;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.UserInterface.Components.Panels;

/// <summary>A row's media can be mid-download; the play control must say so, not invite a refusal.</summary>
public class SelectedSingerInfoPanelQueueRowStatusTests : BunitContext
{
    private const string PlayButtonSelector = ".kh-selected-singer-info-panel__row .kh-split-btn__primary";
    private const string StatusSelector = ".kh-selected-singer-info-panel__status .kh-badge";

    private readonly ISingerQueueService _queue = Substitute.For<ISingerQueueService>();
    private readonly IPerformanceService _performances = Substitute.For<IPerformanceService>();
    private readonly IMediaService _mediaService = Substitute.For<IMediaService>();
    private readonly IPreparedMediaService _prepared = Substitute.For<IPreparedMediaService>();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);
    private readonly KHostUser _singer = new() { Id = Guid.NewGuid(), Name = "Ann" };
    private readonly Performance _performance;
    private readonly Media _media;

    public SelectedSingerInfoPanelQueueRowStatusTests()
    {
        _performance = new Performance { Id = Guid.NewGuid(), SingerId = _singer.Id, MediaId = Guid.NewGuid() };
        _media = new Media { Id = _performance.MediaId, FilePath = "/music/song.mp4", Title = "Song", Status = MediaStatus.Downloading };

        _queue.SelectedUser.Returns(_singer);
        _queue.SelectedUserId.Returns(_singer.Id);
        _performances.ReadQueuedAsync().Returns(_ => [_performance]);
        // A substitute reference, not a snapshot: mutating _media.Status is visible on the next read.
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
        Services.AddSingleton(_prepared);
        Services.AddSingleton(Substitute.For<IUsersService>());
        Services.AddSingleton(Substitute.For<IUserGroupsService>());
        Services.AddSingleton(Substitute.For<IDialogService>());
        Services.AddSingleton(Substitute.For<ITipsService>());
        Services.AddSingleton<IMessageBroker>(_broker);
    }

    [Fact]
    public void DownloadingMedia_ShowsSpinnerInsteadOfPlay_AndDisablesTheButton()
    {
        var panel = Render<SelectedSingerInfoPanel>();

        var button = panel.Find(PlayButtonSelector);

        Assert.NotEmpty(panel.FindAll($"{PlayButtonSelector} .kh-loader__spinner"));
        Assert.Empty(panel.FindAll($"{PlayButtonSelector} .bi-play-fill"));
        Assert.True(button.HasAttribute("disabled"));
    }

    [Fact]
    public void ProcessingMedia_ShowsSpinnerInsteadOfPlay_AndDisablesTheButton()
    {
        _media.Status = MediaStatus.Processing;

        var panel = Render<SelectedSingerInfoPanel>();

        var button = panel.Find(PlayButtonSelector);

        Assert.NotEmpty(panel.FindAll($"{PlayButtonSelector} .kh-loader__spinner"));
        Assert.Empty(panel.FindAll($"{PlayButtonSelector} .bi-play-fill"));
        Assert.True(button.HasAttribute("disabled"));
    }

    [Fact]
    public void ReadyMedia_ShowsThePlayIcon_AndEnablesTheButton()
    {
        _media.Status = MediaStatus.Ready;

        var panel = Render<SelectedSingerInfoPanel>();

        var button = panel.Find(PlayButtonSelector);

        Assert.NotEmpty(panel.FindAll($"{PlayButtonSelector} .bi-play-fill"));
        Assert.Empty(panel.FindAll($"{PlayButtonSelector} .kh-loader__spinner"));
        Assert.False(button.HasAttribute("disabled"));
    }

    [Fact]
    public async Task MediaTurningReady_ReRendersTheRow_WhenMediaLibraryChangedIsPublished()
    {
        var panel = Render<SelectedSingerInfoPanel>();
        Assert.NotEmpty(panel.FindAll($"{PlayButtonSelector} .kh-loader__spinner"));

        _media.Status = MediaStatus.Ready;
        await _broker.PublishAsync(new MediaLibraryChanged());

        panel.WaitForAssertion(() => Assert.NotEmpty(panel.FindAll($"{PlayButtonSelector} .bi-play-fill")));
        Assert.Empty(panel.FindAll($"{PlayButtonSelector} .kh-loader__spinner"));
    }

    /// <summary>A Ready row whose file only a plugin can read has nothing to start until its render
    /// lands. Offered anyway, the click is refused and the host is told to try again, which reads as
    /// the console ignoring them.</summary>
    [Fact]
    public void MediaWaitingOnARender_ShowsSpinnerInsteadOfPlay_AndDisablesTheButton()
    {
        _media.Status = MediaStatus.Ready;
        _prepared.IsWaitingOnARender(_media.FilePath!).Returns(true);

        var panel = Render<SelectedSingerInfoPanel>();

        var button = panel.Find(PlayButtonSelector);

        Assert.NotEmpty(panel.FindAll($"{PlayButtonSelector} .kh-loader__spinner"));
        Assert.Empty(panel.FindAll($"{PlayButtonSelector} .bi-play-fill"));
        Assert.True(button.HasAttribute("disabled"));
    }

    /// <summary>A greyed control with no reason on it is indistinguishable from a broken one.</summary>
    [Fact]
    public void MediaWaitingOnARender_SaysWhyTheButtonIsDisabled()
    {
        _media.Status = MediaStatus.Ready;
        _prepared.IsWaitingOnARender(_media.FilePath!).Returns(true);

        var panel = Render<SelectedSingerInfoPanel>();

        Assert.Contains("getting ready", panel.Find(PlayButtonSelector).GetAttribute("title") ?? "");
    }

    /// <summary>The control this has to keep: an ordinary file being pre-rendered is playable the
    /// whole time, by the transcode that has always been there. Greying it would take away a song
    /// the host could have started at once.</summary>
    [Fact]
    public void MediaBeingPreparedButPlayableAsItIs_KeepsThePlayButton()
    {
        _media.Status = MediaStatus.Ready;
        _prepared.StateFor(_media.FilePath!).Returns(PerformancePreparation.Preparing);
        _prepared.IsWaitingOnARender(_media.FilePath!).Returns(false);

        var panel = Render<SelectedSingerInfoPanel>();

        Assert.NotEmpty(panel.FindAll($"{PlayButtonSelector} .bi-play-fill"));
        Assert.False(panel.Find(PlayButtonSelector).HasAttribute("disabled"));
    }

    /// <summary>The render landing has to reach the row on its own: nothing else redraws it, so the
    /// button would stay greyed over a song that is ready until the host clicked elsewhere.</summary>
    [Fact]
    public async Task ARenderLanding_ReEnablesTheButton_WhenPreparedMediaChangedIsPublished()
    {
        _media.Status = MediaStatus.Ready;
        _prepared.IsWaitingOnARender(_media.FilePath!).Returns(true);

        var panel = Render<SelectedSingerInfoPanel>();
        Assert.True(panel.Find(PlayButtonSelector).HasAttribute("disabled"));

        _prepared.IsWaitingOnARender(_media.FilePath!).Returns(false);
        await _broker.PublishAsync(new PreparedMediaChanged());

        panel.WaitForAssertion(() => Assert.False(panel.Find(PlayButtonSelector).HasAttribute("disabled")));
        Assert.NotEmpty(panel.FindAll($"{PlayButtonSelector} .bi-play-fill"));
    }

    /// <summary>The row is Ready for the whole of its pre-render, so the library status alone
    /// reads as though nothing is happening. The column says what is actually going on instead.
    /// </summary>
    [Fact]
    public void ARowBeingPrepared_ReadsPreparing()
    {
        _media.Status = MediaStatus.Ready;
        _prepared.StateFor(_media.FilePath!).Returns(PerformancePreparation.Preparing);

        var panel = Render<SelectedSingerInfoPanel>();

        Assert.Equal("Preparing", panel.Find(StatusSelector).TextContent.Trim());
    }

    [Fact]
    public void ARowWithNothingRendering_ReadsItsOwnStatus()
    {
        _media.Status = MediaStatus.Ready;
        _prepared.StateFor(_media.FilePath!).Returns(PerformancePreparation.Prepared);

        var panel = Render<SelectedSingerInfoPanel>();

        Assert.Equal("Ready", panel.Find(StatusSelector).TextContent.Trim());
    }

    /// <summary>Broken is something a host has to act on, so a render in flight must not paint
    /// over it. The same holds for an acquisition still running.</summary>
    [Theory]
    [InlineData(MediaStatus.Broken, "Broken")]
    [InlineData(MediaStatus.Downloading, "Downloading")]
    [InlineData(MediaStatus.Processing, "Processing")]
    public void ARowWithSomethingMoreImportantToSay_KeepsSayingIt(MediaStatus status, string expected)
    {
        _media.Status = status;
        _prepared.StateFor(_media.FilePath!).Returns(PerformancePreparation.Preparing);

        var panel = Render<SelectedSingerInfoPanel>();

        Assert.Equal(expected, panel.Find(StatusSelector).TextContent.Trim());
    }

    /// <summary>The spinning glyph beside the title is gone: the status column is the one place
    /// that says a render is happening, so two places cannot disagree about it.</summary>
    [Fact]
    public void ARowBeingPrepared_CarriesNoSpinningGlyph()
    {
        _media.Status = MediaStatus.Ready;
        _prepared.StateFor(_media.FilePath!).Returns(PerformancePreparation.Preparing);

        var panel = Render<SelectedSingerInfoPanel>();

        Assert.Empty(panel.FindAll(".kh-selected-singer-info-panel__preparing"));
        Assert.Empty(panel.FindAll(".kh-selected-singer-info-panel__row .bi-arrow-repeat"));
    }
}
