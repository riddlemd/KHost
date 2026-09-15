using Bunit;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services.Messaging;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using KHost.UserInterface.Components.Pages.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.UserInterface.Components.Pages.Settings;

public class DownloadsManagerPageTests : BunitContext
{
    private const string ProgressTrackSelector = ".kh-downloads-manager__progress-track";
    private const string CancelButtonSelector = ".kh-downloads-manager__actions button";
    private const string RecentRowSelector = ".kh-downloads-manager__recent-row";
    private const string PhaseSelector = ".kh-downloads-manager__phase";
    private const string DetailSelector = ".kh-downloads-manager__detail";

    private readonly IDownloadsService _downloadsService = Substitute.For<IDownloadsService>();
    private readonly IPerformanceService _performanceService = Substitute.For<IPerformanceService>();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);

    public DownloadsManagerPageTests()
    {
        _performanceService.ReadQueuedAsync().Returns(new List<Performance>());

        Services.AddSingleton(_downloadsService);
        Services.AddSingleton(_performanceService);
        Services.AddSingleton<IMessageBroker>(_broker);
    }

    [Fact]
    public void ActiveDownloadingEntry_RendersAProgressBar()
    {
        var mediaId = Guid.NewGuid();
        _downloadsService.Snapshot().Returns([Downloading(mediaId, progress: 0.5)]);

        var cut = Render<DownloadsManagerPage>();

        Assert.Single(cut.FindAll(ProgressTrackSelector));
    }

    [Fact]
    public void ActiveEntry_NoReportedProgress_RendersASpinnerInsteadOfAProgressBar()
    {
        var mediaId = Guid.NewGuid();
        _downloadsService.Snapshot().Returns([Downloading(mediaId, progress: null)]);

        var cut = Render<DownloadsManagerPage>();

        Assert.Empty(cut.FindAll(ProgressTrackSelector));
        Assert.Single(cut.FindAll(".kh-loader"));
    }

    [Fact]
    public async Task CancelButton_Clicked_CallsCancelAsync()
    {
        var mediaId = Guid.NewGuid();
        _downloadsService.Snapshot().Returns([Downloading(mediaId, progress: 0.5)]);

        var cut = Render<DownloadsManagerPage>();
        await cut.InvokeAsync(() => cut.Find(CancelButtonSelector).Click());

        await _downloadsService.Received(1).CancelAsync(mediaId);
    }

    [Fact]
    public async Task CancelButton_Clicked_DequeuesAnyQueuedPerformanceForThatMedia()
    {
        var mediaId = Guid.NewGuid();
        _downloadsService.Snapshot().Returns([Downloading(mediaId, progress: 0.5)]);
        var queued = new Performance { Id = Guid.NewGuid(), SingerId = Guid.NewGuid(), MediaId = mediaId, QueuePosition = 1 };
        var otherMediaQueued = new Performance { Id = Guid.NewGuid(), SingerId = Guid.NewGuid(), MediaId = Guid.NewGuid(), QueuePosition = 2 };
        _performanceService.ReadQueuedAsync().Returns(new List<Performance> { queued, otherMediaQueued });

        var cut = Render<DownloadsManagerPage>();
        await cut.InvokeAsync(() => cut.Find(CancelButtonSelector).Click());

        await _performanceService.Received(1).DeleteAsync(queued.Id);
        await _performanceService.DidNotReceive().DeleteAsync(otherMediaQueued.Id);
    }

    [Fact]
    public void RecentSection_SettledEntries_RendersTheirState()
    {
        _downloadsService.Snapshot().Returns([Settled(Guid.NewGuid(), DownloadState.Failed)]);

        var cut = Render<DownloadsManagerPage>();

        var row = cut.Find(RecentRowSelector);
        Assert.Contains("Failed", row.TextContent);
    }

    [Fact]
    public void BothSections_Empty_ShowEmptyStates()
    {
        _downloadsService.Snapshot().Returns([]);

        var cut = Render<DownloadsManagerPage>();

        Assert.Equal(2, cut.FindAll(".kh-panel--empty").Count);
    }

    [Fact]
    public async Task DownloadsChanged_ReRendersWithTheNewSnapshot()
    {
        var mediaId = Guid.NewGuid();
        _downloadsService.Snapshot().Returns([]);
        var cut = Render<DownloadsManagerPage>();
        Assert.Empty(cut.FindAll(ProgressTrackSelector));

        _downloadsService.Snapshot().Returns([Downloading(mediaId, progress: 0.2)]);
        await _broker.PublishAsync(new DownloadsChanged());

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(ProgressTrackSelector)));
    }

    private static DownloadInfo Downloading(Guid mediaId, double? progress) => new()
    {
        MediaId = mediaId,
        Title = "Song Title",
        Artist = "Song Artist",
        Source = "Test Plugin",
        StartedUtc = DateTime.UtcNow,
        State = DownloadState.Downloading,
        Progress = progress,
    };

    private static DownloadInfo Settled(Guid mediaId, DownloadState state) => new()
    {
        MediaId = mediaId,
        Title = "Settled Title",
        StartedUtc = DateTime.UtcNow,
        State = state,
    };
    [Theory]
    [InlineData(DownloadPhase.Fetching, "Fetching")]
    [InlineData(DownloadPhase.Processing, "Processing")]
    public void ActiveEntry_NamesTheHalfItsProgressIsMeasuring(DownloadPhase phase, string expected)
    {
        var mediaId = Guid.NewGuid();
        _downloadsService.Snapshot().Returns([Downloading(mediaId, progress: 0.68) with { Phase = phase }]);

        var cut = Render<DownloadsManagerPage>();

        Assert.Contains(expected, cut.Find(PhaseSelector).TextContent);
    }

    [Fact]
    public void ActiveEntry_CountingBytes_ShowsHowMuchOfHowMuch()
    {
        var mediaId = Guid.NewGuid();
        _downloadsService.Snapshot().Returns([Downloading(mediaId, progress: 0.5) with
        {
            BytesReceived = 14_889_779,
            TotalBytes = 24_222_925,
        }]);

        var cut = Render<DownloadsManagerPage>();

        var phase = cut.Find(PhaseSelector).TextContent;
        Assert.Contains("14.2 MB", phase);
        Assert.Contains("23.1 MB", phase);
    }

    [Fact]
    public void ActiveEntry_CountingNoBytes_ShowsThePhaseAloneWithNoTrailingSeparator()
    {
        var mediaId = Guid.NewGuid();
        _downloadsService.Snapshot().Returns([Downloading(mediaId, progress: 0.5)]);

        var cut = Render<DownloadsManagerPage>();

        Assert.Equal("Fetching", cut.Find(PhaseSelector).TextContent.Trim());
    }

    [Fact]
    public void SettledFailure_ShowsItsReasonRatherThanTheBadgeAlone()
    {
        var mediaId = Guid.NewGuid();
        _downloadsService.Snapshot().Returns([Settled(mediaId, DownloadState.Failed) with
        {
            Reason = "checksum did not match",
            SettledUtc = DateTime.UtcNow,
        }]);

        var cut = Render<DownloadsManagerPage>();

        Assert.Equal("checksum did not match", cut.Find(DetailSelector).TextContent.Trim());
    }

    [Fact]
    public void SettledCompletion_ShowsWhatItCostInstead()
    {
        var mediaId = Guid.NewGuid();
        var started = DateTime.UtcNow.AddSeconds(-124);
        _downloadsService.Snapshot().Returns([Settled(mediaId, DownloadState.Completed) with
        {
            StartedUtc = started,
            SettledUtc = started.AddSeconds(124),
            BytesReceived = 24_222_925,
        }]);

        var cut = Render<DownloadsManagerPage>();

        var detail = cut.Find(DetailSelector).TextContent;
        Assert.Contains("2:04", detail);
        Assert.Contains("23.1 MB", detail);
    }

    [Fact]
    public void CancelAll_CancelsEveryActiveDownloadAndDequeuesTheirPerformances()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        _downloadsService.Snapshot().Returns([Downloading(first, 0.1), Downloading(second, 0.2)]);
        _performanceService.ReadQueuedAsync().Returns(
        [
            new Performance { Id = Guid.NewGuid(), MediaId = first },
            new Performance { Id = Guid.NewGuid(), MediaId = Guid.NewGuid() },
        ]);

        var cut = Render<DownloadsManagerPage>();
        cut.Find(".kh-card__header .kh-button--danger").Click();

        _downloadsService.Received(1).CancelAll();
        _performanceService.Received(1).DeleteAsync(Arg.Any<Guid>());
    }

}
