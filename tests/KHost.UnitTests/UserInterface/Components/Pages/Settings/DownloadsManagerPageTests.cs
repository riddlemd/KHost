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
}
