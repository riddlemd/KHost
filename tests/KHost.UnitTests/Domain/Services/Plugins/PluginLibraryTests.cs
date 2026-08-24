using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.Abstractions.Services;
using KHost.Domain.Services;
using KHost.Domain.Services.Messaging;
using KHost.Domain.Services.Plugins;
using KHost.Plugins.Sdk.Messaging.Messages;
using KHost.Plugins.Sdk.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KHost.UnitTests.Domain.Services.Plugins;

public class PluginLibraryTests
{
    private readonly ILogger<PluginLibrary> _logger = Substitute.For<ILogger<PluginLibrary>>();
    private readonly IMediaRepository _repository = Substitute.For<IMediaRepository>();
    private readonly IMediaService _mediaService = Substitute.For<IMediaService>();
    private readonly ISingerQueueService _singerQueueService = Substitute.For<ISingerQueueService>();
    private readonly IPerformanceService _performanceService = Substitute.For<IPerformanceService>();
    private readonly IOptionsMonitor<PluginLibrary.ServiceOptions> _options = Substitute.For<IOptionsMonitor<PluginLibrary.ServiceOptions>>();
    private readonly DownloadsService _downloads = new(new MessageBroker(NullLogger<MessageBroker>.Instance));
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);
    private readonly PluginLibrary _service;

    public PluginLibraryTests()
    {
        _mediaService.CreateAsync(Arg.Any<Media>()).Returns(call => call.ArgAt<Media>(0));
        _performanceService.CreateAndEnqueueAsync(Arg.Any<Performance>())
            .Returns(call => Task.FromResult<Performance?>(call.ArgAt<Performance>(0)));
        _options.CurrentValue.Returns(new PluginLibrary.ServiceOptions());

        _service = new PluginLibrary(_logger, _repository, _mediaService, _singerQueueService, _performanceService, _options, _downloads, _broker);
    }

    [Fact]
    public void MediaDirectory_ConfiguredValue_ReturnsItTrimmed()
    {
        _options.CurrentValue.Returns(new PluginLibrary.ServiceOptions { MediaDirectory = "  /data/karaoke  " });

        Assert.Equal("/data/karaoke", _service.MediaDirectory);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MediaDirectory_BlankOrMissing_FallsBackToUserProfileKaraoke(string? configured)
    {
        _options.CurrentValue.Returns(new PluginLibrary.ServiceOptions { MediaDirectory = configured });

        var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "karaoke");
        Assert.Equal(expected, _service.MediaDirectory);
    }

    [Fact]
    public async Task ImportAsync_NewFilePath_CreatesRowWithRequestFields()
    {
        _repository.FindByFilePathAsync(Arg.Any<string>()).Returns((Media?)null);
        var request = new MediaImportRequest
        {
            FilePath = "/downloads/song.mp4",
            Title = "Song Title",
            Artist = "Song Artist",
            Duration = TimeSpan.FromMinutes(3),
            Notes = "from plugin",
        };

        await _service.ImportAsync(request);

        await _mediaService.Received(1).CreateAsync(Arg.Is<Media>(m =>
            m.FilePath == "/downloads/song.mp4" &&
            m.Title == "Song Title" &&
            m.Artist == "Song Artist" &&
            m.Duration == TimeSpan.FromMinutes(3) &&
            m.Notes == "from plugin" &&
            m.Status == MediaStatus.Ready));
    }

    [Fact]
    public async Task ImportAsync_NewFilePath_ReturnsCreatedRowId()
    {
        _repository.FindByFilePathAsync(Arg.Any<string>()).Returns((Media?)null);
        var request = new MediaImportRequest { FilePath = "/downloads/song.mp4", Title = "Song Title" };

        var id = await _service.ImportAsync(request);

        var created = _mediaService.ReceivedCalls().Single().GetArguments()[0] as Media;
        Assert.Equal(created!.Id, id);
    }

    [Fact]
    public async Task ImportAsync_SameFilePathTwice_ReturnsFirstIdAndDoesNotCreateAgain()
    {
        var existing = new Media { Id = Guid.NewGuid(), FilePath = "/downloads/song.mp4", Title = "Song Title" };
        _repository.FindByFilePathAsync("/downloads/song.mp4").Returns(existing);
        var request = new MediaImportRequest { FilePath = "/downloads/song.mp4", Title = "Song Title" };

        var id = await _service.ImportAsync(request);

        Assert.Equal(existing.Id, id);
        await _mediaService.DidNotReceive().CreateAsync(Arg.Any<Media>());
    }

    [Fact]
    public async Task BeginImportAsync_NewFilePath_CreatesRowWithDownloadingStatus()
    {
        _repository.FindByFilePathAsync(Arg.Any<string>()).Returns((Media?)null);
        var request = new MediaImportRequest { FilePath = "/downloads/song.mp4", Title = "Song Title" };

        await _service.BeginImportAsync(request);

        await _mediaService.Received(1).CreateAsync(Arg.Is<Media>(m =>
            m.FilePath == "/downloads/song.mp4" &&
            m.Title == "Song Title" &&
            m.Status == MediaStatus.Downloading));
    }

    [Fact]
    public async Task BeginImportAsync_SameFilePathTwice_ReturnsFirstIdAndDoesNotCreateAgain()
    {
        var existing = new Media { Id = Guid.NewGuid(), FilePath = "/downloads/song.mp4", Title = "Song Title" };
        _repository.FindByFilePathAsync("/downloads/song.mp4").Returns(existing);
        var request = new MediaImportRequest { FilePath = "/downloads/song.mp4", Title = "Song Title" };

        var ticket = await _service.BeginImportAsync(request);

        Assert.Equal(existing.Id, ticket.MediaId);
        await _mediaService.DidNotReceive().CreateAsync(Arg.Any<Media>());
    }

    [Fact]
    public async Task BeginImportAsync_ExistingRowAlreadyReady_DoesNotRegressToDownloading()
    {
        var existing = new Media
        {
            Id = Guid.NewGuid(),
            FilePath = "/downloads/song.mp4",
            Title = "Song Title",
            Status = MediaStatus.Ready,
        };
        _repository.FindByFilePathAsync("/downloads/song.mp4").Returns(existing);
        var request = new MediaImportRequest { FilePath = "/downloads/song.mp4", Title = "Song Title" };

        await _service.BeginImportAsync(request);

        Assert.Equal(MediaStatus.Ready, existing.Status);
        await _mediaService.DidNotReceive().UpdateAsync(Arg.Any<Media>());
    }

    [Fact]
    public async Task BeginImportAsync_NewRow_TicketTokenFiresWhenCancelAsyncIsCalled()
    {
        _repository.FindByFilePathAsync(Arg.Any<string>()).Returns((Media?)null);
        var created = new Media { Id = Guid.NewGuid(), FilePath = "/downloads/song.mp4", Title = "Song Title" };
        _mediaService.CreateAsync(Arg.Any<Media>()).Returns(created);
        var request = new MediaImportRequest { FilePath = "/downloads/song.mp4", Title = "Song Title" };

        var ticket = await _service.BeginImportAsync(request);

        Assert.False(ticket.Cancellation.IsCancellationRequested);

        await _downloads.CancelAsync(created.Id);

        Assert.True(ticket.Cancellation.IsCancellationRequested);
    }

    [Fact]
    public async Task BeginImportAsync_ExistingInFlightRow_ReusesTheSameTokenAcrossCalls()
    {
        var media = new Media { Id = Guid.NewGuid(), FilePath = "/downloads/song.mp4", Title = "Song Title", Status = MediaStatus.Downloading };
        _repository.FindByFilePathAsync("/downloads/song.mp4").Returns((Media?)null);
        _mediaService.CreateAsync(Arg.Any<Media>()).Returns(media);
        var request = new MediaImportRequest { FilePath = "/downloads/song.mp4", Title = "Song Title" };

        var first = await _service.BeginImportAsync(request);

        // The row now exists, so the second Begin for the same path finds it rather than creating.
        _repository.FindByFilePathAsync("/downloads/song.mp4").Returns(media);
        var second = await _service.BeginImportAsync(request);

        Assert.False(first.Cancellation.IsCancellationRequested);
        Assert.False(second.Cancellation.IsCancellationRequested);

        await _downloads.CancelAsync(media.Id);

        Assert.True(first.Cancellation.IsCancellationRequested);
        Assert.True(second.Cancellation.IsCancellationRequested);
    }

    [Fact]
    public async Task BeginImportAsync_ExistingSettledRow_ReturnsNoneToken()
    {
        var existing = new Media
        {
            Id = Guid.NewGuid(),
            FilePath = "/downloads/song.mp4",
            Title = "Song Title",
            Status = MediaStatus.Ready,
        };
        _repository.FindByFilePathAsync("/downloads/song.mp4").Returns(existing);
        var request = new MediaImportRequest { FilePath = "/downloads/song.mp4", Title = "Song Title" };

        var ticket = await _service.BeginImportAsync(request);

        Assert.Equal(CancellationToken.None, ticket.Cancellation);
    }

    [Fact]
    public async Task CompleteImportAsync_ExistingRow_SetsStatusToReady()
    {
        var media = new Media { Id = Guid.NewGuid(), FilePath = "/downloads/song.mp4", Title = "Song Title", Status = MediaStatus.Downloading };
        _mediaService.ReadAsync(media.Id).Returns(media);

        await _service.CompleteImportAsync(media.Id);

        await _mediaService.Received(1).UpdateAsync(Arg.Is<Media>(m => m.Id == media.Id && m.Status == MediaStatus.Ready));
    }

    [Fact]
    public async Task CompleteImportAsync_AnnouncesMediaLibraryChanged()
    {
        // A real MediaService rather than the substitute: BaseRepositoryService.UpdateAsync is
        // what actually publishes MediaLibraryChanged, so this proves PluginLibrary reaches it rather than
        // asserting on a mock that we would have to wire the same behaviour into by hand.
        var repository = Substitute.For<IMediaRepository>();
        var mediaService = new MediaService(NullLogger<MediaService>.Instance, repository, _broker);
        var media = new Media { Id = Guid.NewGuid(), FilePath = "/downloads/song.mp4", Title = "Song Title", Status = MediaStatus.Downloading };
        repository.ReadAsync(media.Id).Returns(media);

        var service = new PluginLibrary(_logger, repository, mediaService, _singerQueueService, _performanceService, _options, _downloads, _broker);
        var raised = 0;
        using var subscription = _broker.Subscribe<MediaLibraryChanged>(_ => raised++);

        await service.CompleteImportAsync(media.Id);

        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task CompleteImportAsync_NoSuchMedia_DoesNotUpdate()
    {
        _mediaService.ReadAsync(Arg.Any<Guid>()).Returns((Media?)null);

        await _service.CompleteImportAsync(Guid.NewGuid());

        await _mediaService.DidNotReceive().UpdateAsync(Arg.Any<Media>());
    }

    [Fact]
    public async Task FailImportAsync_ExistingRow_SetsStatusToBroken()
    {
        var media = new Media { Id = Guid.NewGuid(), FilePath = "/downloads/song.mp4", Title = "Song Title", Status = MediaStatus.Downloading };
        _mediaService.ReadAsync(media.Id).Returns(media);

        await _service.FailImportAsync(media.Id);

        await _mediaService.Received(1).UpdateAsync(Arg.Is<Media>(m => m.Id == media.Id && m.Status == MediaStatus.Broken));
    }

    [Fact]
    public async Task FailImportAsync_AnnouncesMediaLibraryChanged()
    {
        var repository = Substitute.For<IMediaRepository>();
        var mediaService = new MediaService(NullLogger<MediaService>.Instance, repository, _broker);
        var media = new Media { Id = Guid.NewGuid(), FilePath = "/downloads/song.mp4", Title = "Song Title", Status = MediaStatus.Downloading };
        repository.ReadAsync(media.Id).Returns(media);

        var service = new PluginLibrary(_logger, repository, mediaService, _singerQueueService, _performanceService, _options, _downloads, _broker);
        var raised = 0;
        using var subscription = _broker.Subscribe<MediaLibraryChanged>(_ => raised++);

        await service.FailImportAsync(media.Id);

        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task DiscardImportAsync_DownloadingRow_DeletesIt()
    {
        var media = new Media { Id = Guid.NewGuid(), FilePath = "/downloads/song.mp4", Title = "Song Title", Status = MediaStatus.Downloading };
        _mediaService.ReadAsync(media.Id).Returns(media);

        await _service.DiscardImportAsync(media.Id);

        await _mediaService.Received(1).DeleteAsync(media.Id);
    }

    [Theory]
    [InlineData(MediaStatus.Ready)]
    [InlineData(MediaStatus.Broken)]
    public async Task DiscardImportAsync_SettledRow_NeverDeletesIt(MediaStatus status)
    {
        var media = new Media { Id = Guid.NewGuid(), FilePath = "/downloads/song.mp4", Title = "Song Title", Status = status };
        _mediaService.ReadAsync(media.Id).Returns(media);

        await _service.DiscardImportAsync(media.Id);

        await _mediaService.DidNotReceive().DeleteAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task DiscardImportAsync_NoSuchMedia_NoOps()
    {
        _mediaService.ReadAsync(Arg.Any<Guid>()).Returns((Media?)null);

        await _service.DiscardImportAsync(Guid.NewGuid());

        await _mediaService.DidNotReceive().DeleteAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task DiscardImportAsync_AnnouncesMediaLibraryChanged()
    {
        var repository = Substitute.For<IMediaRepository>();
        var mediaService = new MediaService(NullLogger<MediaService>.Instance, repository, _broker);
        var media = new Media { Id = Guid.NewGuid(), FilePath = "/downloads/song.mp4", Title = "Song Title", Status = MediaStatus.Downloading };
        repository.ReadAsync(media.Id).Returns(media);
        repository.DeleteAsync(media.Id).Returns(true);

        var service = new PluginLibrary(_logger, repository, mediaService, _singerQueueService, _performanceService, _options, _downloads, _broker);
        var raised = 0;
        using var subscription = _broker.Subscribe<MediaLibraryChanged>(_ => raised++);

        await service.DiscardImportAsync(media.Id);

        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task CompleteImportAsync_RemovesRegistration_SoALaterCancelIsANoOp()
    {
        _repository.FindByFilePathAsync(Arg.Any<string>()).Returns((Media?)null);
        var media = new Media { Id = Guid.NewGuid(), FilePath = "/downloads/song.mp4", Title = "Song Title" };
        _mediaService.CreateAsync(Arg.Any<Media>()).Returns(media);
        var ticket = await _service.BeginImportAsync(new MediaImportRequest { FilePath = "/downloads/song.mp4", Title = "Song Title" });
        _mediaService.ReadAsync(media.Id).Returns(media);

        await _service.CompleteImportAsync(media.Id);
        await _downloads.CancelAsync(media.Id);

        Assert.False(ticket.Cancellation.IsCancellationRequested);
    }

    [Fact]
    public async Task FailImportAsync_RemovesRegistration_SoALaterCancelIsANoOp()
    {
        _repository.FindByFilePathAsync(Arg.Any<string>()).Returns((Media?)null);
        var media = new Media { Id = Guid.NewGuid(), FilePath = "/downloads/song.mp4", Title = "Song Title" };
        _mediaService.CreateAsync(Arg.Any<Media>()).Returns(media);
        var ticket = await _service.BeginImportAsync(new MediaImportRequest { FilePath = "/downloads/song.mp4", Title = "Song Title" });
        _mediaService.ReadAsync(media.Id).Returns(media);

        await _service.FailImportAsync(media.Id);
        await _downloads.CancelAsync(media.Id);

        Assert.False(ticket.Cancellation.IsCancellationRequested);
    }

    [Fact]
    public async Task DiscardImportAsync_RemovesRegistration_SoALaterCancelIsANoOp()
    {
        _repository.FindByFilePathAsync(Arg.Any<string>()).Returns((Media?)null);
        var media = new Media { Id = Guid.NewGuid(), FilePath = "/downloads/song.mp4", Title = "Song Title", Status = MediaStatus.Downloading };
        _mediaService.CreateAsync(Arg.Any<Media>()).Returns(media);
        var ticket = await _service.BeginImportAsync(new MediaImportRequest { FilePath = "/downloads/song.mp4", Title = "Song Title" });
        _mediaService.ReadAsync(media.Id).Returns(media);

        await _service.DiscardImportAsync(media.Id);
        await _downloads.CancelAsync(media.Id);

        Assert.False(ticket.Cancellation.IsCancellationRequested);
    }

    [Fact]
    public async Task CancelAsync_NothingRegistered_DoesNotThrow()
    {
        await _downloads.CancelAsync(Guid.NewGuid());
    }

    [Fact]
    public async Task CancelAll_CancelsEveryRegisteredToken()
    {
        _repository.FindByFilePathAsync(Arg.Any<string>()).Returns((Media?)null);
        var mediaA = new Media { Id = Guid.NewGuid(), FilePath = "/downloads/a.mp4", Title = "A" };
        var mediaB = new Media { Id = Guid.NewGuid(), FilePath = "/downloads/b.mp4", Title = "B" };
        _mediaService.CreateAsync(Arg.Is<Media>(m => m.FilePath == "/downloads/a.mp4")).Returns(mediaA);
        _mediaService.CreateAsync(Arg.Is<Media>(m => m.FilePath == "/downloads/b.mp4")).Returns(mediaB);

        var ticketA = await _service.BeginImportAsync(new MediaImportRequest { FilePath = "/downloads/a.mp4", Title = "A" });
        var ticketB = await _service.BeginImportAsync(new MediaImportRequest { FilePath = "/downloads/b.mp4", Title = "B" });

        _downloads.CancelAll();

        Assert.True(ticketA.Cancellation.IsCancellationRequested);
        Assert.True(ticketB.Cancellation.IsCancellationRequested);
    }

    [Fact]
    public async Task EnqueueAsync_NoSingerSelected_DoesNotEnqueue()
    {
        _singerQueueService.SelectedUserId.Returns((Guid?)null);

        await _service.EnqueueAsync(Guid.NewGuid());

        await _performanceService.DidNotReceive().CreateAndEnqueueAsync(Arg.Any<Performance>());
    }

    [Fact]
    public async Task EnqueueAsync_SingerSelected_PassesSelectedSingerAndMediaId()
    {
        var singerId = Guid.NewGuid();
        var mediaId = Guid.NewGuid();
        _singerQueueService.SelectedUserId.Returns(singerId);

        await _service.EnqueueAsync(mediaId);

        await _performanceService.Received(1).CreateAndEnqueueAsync(
            Arg.Is<Performance>(p => p.MediaId == mediaId && p.SingerId == singerId));
    }

    [Fact]
    public async Task BeginImportAsync_NewRow_RegistersDownloadMetadataFromTheRequest()
    {
        _repository.FindByFilePathAsync(Arg.Any<string>()).Returns((Media?)null);
        var created = new Media { Id = Guid.NewGuid(), FilePath = "/downloads/song.mp4", Title = "Song Title" };
        _mediaService.CreateAsync(Arg.Any<Media>()).Returns(created);
        var request = new MediaImportRequest
        {
            FilePath = "/downloads/song.mp4",
            Title = "Song Title",
            Artist = "Song Artist",
            Source = "My Plugin",
        };

        await _service.BeginImportAsync(request);

        var registered = Assert.Single(_downloads.Snapshot());
        Assert.Equal(created.Id, registered.MediaId);
        Assert.Equal("Song Title", registered.Title);
        Assert.Equal("Song Artist", registered.Artist);
        Assert.Equal("My Plugin", registered.Source);
    }

    [Fact]
    public async Task ReportDownloadProgressAsync_DelegatesToDownloadsService()
    {
        _repository.FindByFilePathAsync(Arg.Any<string>()).Returns((Media?)null);
        var created = new Media { Id = Guid.NewGuid(), FilePath = "/downloads/song.mp4", Title = "Song Title" };
        _mediaService.CreateAsync(Arg.Any<Media>()).Returns(created);
        await _service.BeginImportAsync(new MediaImportRequest { FilePath = "/downloads/song.mp4", Title = "Song Title" });

        await _service.ReportDownloadProgressAsync(created.Id, 0.42);

        Assert.Equal(0.42, Assert.Single(_downloads.Snapshot()).Progress);
    }

    [Fact]
    public async Task ReportDownloadProgressAsync_UnknownMediaId_NoOps()
    {
        // Nothing to assert against but that it does not throw — DownloadsService itself already
        // covers the silent-no-op behaviour for an id it never registered.
        await _service.ReportDownloadProgressAsync(Guid.NewGuid(), 0.5);
    }
}
