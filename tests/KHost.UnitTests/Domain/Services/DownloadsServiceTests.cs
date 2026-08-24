using KHost.Abstractions.Models;
using KHost.Domain.Services;
using KHost.Domain.Services.Messaging;
using KHost.Plugins.Sdk.Messaging.Messages;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.Domain.Services;

public class DownloadsServiceTests
{
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);
    private readonly DownloadsService _service;

    public DownloadsServiceTests()
    {
        _service = new DownloadsService(_broker);
    }

    [Fact]
    public void Register_NewMedia_AddsAnActiveDownloadingEntry()
    {
        var mediaId = Guid.NewGuid();

        _service.Register(mediaId, "Title", "Artist", "Source");

        var entry = Assert.Single(_service.Snapshot());
        Assert.Equal(mediaId, entry.MediaId);
        Assert.Equal("Title", entry.Title);
        Assert.Equal("Artist", entry.Artist);
        Assert.Equal("Source", entry.Source);
        Assert.Equal(DownloadState.Downloading, entry.State);
        Assert.Null(entry.Progress);
    }

    [Fact]
    public void Register_AnnouncesDownloadsChanged()
    {
        var raised = 0;
        using var subscription = _broker.Subscribe<DownloadsChanged>(_ => raised++);

        _service.Register(Guid.NewGuid(), "Title", "Artist", "Source");

        Assert.Equal(1, raised);
    }

    [Fact]
    public void TokenForInFlight_AlreadyRegistered_ReusesTheSameToken()
    {
        var mediaId = Guid.NewGuid();
        var first = _service.Register(mediaId, "Title", "Artist", "Source");

        var second = _service.TokenForInFlight(mediaId, "Title", "Artist", "Source");

        Assert.Equal(first, second);
    }

    [Fact]
    public void TokenForInFlight_NothingRegistered_RegistersFromTheGivenMetadata()
    {
        var mediaId = Guid.NewGuid();

        _service.TokenForInFlight(mediaId, "Resumed Title", "Resumed Artist", "Resumed Source");

        var entry = Assert.Single(_service.Snapshot());
        Assert.Equal("Resumed Title", entry.Title);
        Assert.Equal(DownloadState.Downloading, entry.State);
    }

    [Theory]
    [InlineData(DownloadState.Completed)]
    [InlineData(DownloadState.Failed)]
    [InlineData(DownloadState.Cancelled)]
    public void Settle_ActiveEntry_MovesItToRecentWithTheGivenState(DownloadState state)
    {
        var mediaId = Guid.NewGuid();
        _service.Register(mediaId, "Title", "Artist", "Source");

        _service.Settle(mediaId, state);

        var entry = Assert.Single(_service.Snapshot());
        Assert.Equal(state, entry.State);
        Assert.DoesNotContain(_service.Snapshot(), d => d.State == DownloadState.Downloading);
    }

    [Fact]
    public void Settle_ActiveEntry_AnnouncesDownloadsChanged()
    {
        var mediaId = Guid.NewGuid();
        _service.Register(mediaId, "Title", "Artist", "Source");
        var raised = 0;
        using var subscription = _broker.Subscribe<DownloadsChanged>(_ => raised++);

        _service.Settle(mediaId, DownloadState.Completed);

        Assert.Equal(1, raised);
    }

    [Fact]
    public void Settle_UnknownId_IsANoOp()
    {
        var raised = 0;
        using var subscription = _broker.Subscribe<DownloadsChanged>(_ => raised++);

        _service.Settle(Guid.NewGuid(), DownloadState.Completed);

        Assert.Equal(0, raised);
        Assert.Empty(_service.Snapshot());
    }

    [Fact]
    public void Settle_AlreadySettledId_IsANoOp()
    {
        var mediaId = Guid.NewGuid();
        _service.Register(mediaId, "Title", "Artist", "Source");
        _service.Settle(mediaId, DownloadState.Completed);
        var raised = 0;
        using var subscription = _broker.Subscribe<DownloadsChanged>(_ => raised++);

        _service.Settle(mediaId, DownloadState.Failed);

        Assert.Equal(0, raised);
        Assert.Equal(DownloadState.Completed, Assert.Single(_service.Snapshot()).State);
    }

    [Fact]
    public void Settle_DownloadingState_IsIgnored()
    {
        var mediaId = Guid.NewGuid();
        _service.Register(mediaId, "Title", "Artist", "Source");

        _service.Settle(mediaId, DownloadState.Downloading);

        Assert.Equal(DownloadState.Downloading, Assert.Single(_service.Snapshot()).State);
    }

    [Fact]
    public async Task CancelAsync_ActiveEntry_FiresTheTokenAndMarksCancelled()
    {
        var mediaId = Guid.NewGuid();
        var token = _service.Register(mediaId, "Title", "Artist", "Source");

        await _service.CancelAsync(mediaId);

        Assert.True(token.IsCancellationRequested);
        Assert.Equal(DownloadState.Cancelled, Assert.Single(_service.Snapshot()).State);
    }

    [Fact]
    public async Task CancelAsync_NothingRegistered_DoesNotThrowOrRaise()
    {
        var raised = 0;
        using var subscription = _broker.Subscribe<DownloadsChanged>(_ => raised++);

        await _service.CancelAsync(Guid.NewGuid());

        Assert.Equal(0, raised);
    }

    [Fact]
    public async Task CancelAsync_AlreadySettled_IsANoOp()
    {
        // Proves the cancel(page)->dequeue->cancel(PerformanceService) path can't loop: a second
        // CancelAsync for an id already settled finds nothing and does not re-raise.
        var mediaId = Guid.NewGuid();
        _service.Register(mediaId, "Title", "Artist", "Source");
        await _service.CancelAsync(mediaId);
        var raised = 0;
        using var subscription = _broker.Subscribe<DownloadsChanged>(_ => raised++);

        await _service.CancelAsync(mediaId);

        Assert.Equal(0, raised);
    }

    [Fact]
    public void CancelAll_CancelsEveryActiveToken_AndMarksThemAllCancelled()
    {
        var tokenA = _service.Register(Guid.NewGuid(), "A", "", "");
        var tokenB = _service.Register(Guid.NewGuid(), "B", "", "");

        _service.CancelAll();

        Assert.True(tokenA.IsCancellationRequested);
        Assert.True(tokenB.IsCancellationRequested);
        Assert.All(_service.Snapshot(), d => Assert.Equal(DownloadState.Cancelled, d.State));
    }

    [Fact]
    public void CancelAll_NothingActive_DoesNotRaise()
    {
        var raised = 0;
        using var subscription = _broker.Subscribe<DownloadsChanged>(_ => raised++);

        _service.CancelAll();

        Assert.Equal(0, raised);
    }

    [Theory]
    [InlineData(-0.5, 0)]
    [InlineData(1.5, 1)]
    public void ReportProgress_OutOfRange_ClampsToZeroOrOne(double reported, double expected)
    {
        var mediaId = Guid.NewGuid();
        _service.Register(mediaId, "Title", "Artist", "Source");

        _service.ReportProgress(mediaId, reported);

        Assert.Equal(expected, Assert.Single(_service.Snapshot()).Progress);
    }

    [Fact]
    public void ReportProgress_UnknownId_IsASilentNoOp()
    {
        var raised = 0;
        using var subscription = _broker.Subscribe<DownloadsChanged>(_ => raised++);

        _service.ReportProgress(Guid.NewGuid(), 0.5);

        Assert.Equal(0, raised);
    }

    [Fact]
    public void ReportProgress_SettledId_IsASilentNoOp()
    {
        var mediaId = Guid.NewGuid();
        _service.Register(mediaId, "Title", "Artist", "Source");
        _service.Settle(mediaId, DownloadState.Completed);
        var raised = 0;
        using var subscription = _broker.Subscribe<DownloadsChanged>(_ => raised++);

        _service.ReportProgress(mediaId, 0.5);

        Assert.Equal(0, raised);
        Assert.Null(Assert.Single(_service.Snapshot()).Progress);
    }

    [Fact]
    public void ReportProgress_IntegerPercentChanges_AnnouncesDownloadsChanged()
    {
        var mediaId = Guid.NewGuid();
        _service.Register(mediaId, "Title", "Artist", "Source");
        var raised = 0;
        using var subscription = _broker.Subscribe<DownloadsChanged>(_ => raised++);

        _service.ReportProgress(mediaId, 0.5);

        Assert.Equal(1, raised);
        Assert.Equal(0.5, Assert.Single(_service.Snapshot()).Progress);
    }

    [Fact]
    public void ReportProgress_SameIntegerPercent_DoesNotAnnounceDownloadsChangedButStillRecordsTheValue()
    {
        var mediaId = Guid.NewGuid();
        _service.Register(mediaId, "Title", "Artist", "Source");
        _service.ReportProgress(mediaId, 0.501);
        var raised = 0;
        using var subscription = _broker.Subscribe<DownloadsChanged>(_ => raised++);

        _service.ReportProgress(mediaId, 0.504);

        Assert.Equal(0, raised);
        Assert.Equal(0.504, Assert.Single(_service.Snapshot()).Progress);
    }

    [Fact]
    public void Snapshot_MoreThanFiftySettled_KeepsOnlyTheFiftyMostRecent()
    {
        for (var i = 0; i < 55; i++)
        {
            var mediaId = Guid.NewGuid();
            _service.Register(mediaId, $"Title{i}", "", "");
            _service.Settle(mediaId, DownloadState.Completed);
        }

        Assert.Equal(50, _service.Snapshot().Count);
    }

    [Fact]
    public void Snapshot_MoreThanFiftySettled_NewestSettledIsFirst()
    {
        Guid last = default;
        for (var i = 0; i < 55; i++)
        {
            last = Guid.NewGuid();
            _service.Register(last, $"Title{i}", "", "");
            _service.Settle(last, DownloadState.Completed);
        }

        Assert.Equal(last, _service.Snapshot()[0].MediaId);
    }

    [Fact]
    public void Snapshot_ActiveAndSettled_ReturnsBoth()
    {
        var activeId = Guid.NewGuid();
        var settledId = Guid.NewGuid();
        _service.Register(activeId, "Active", "", "");
        _service.Register(settledId, "Settled", "", "");
        _service.Settle(settledId, DownloadState.Failed);

        var snapshot = _service.Snapshot();

        Assert.Contains(snapshot, d => d.MediaId == activeId && d.State == DownloadState.Downloading);
        Assert.Contains(snapshot, d => d.MediaId == settledId && d.State == DownloadState.Failed);
    }
}
