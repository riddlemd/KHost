using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.Abstractions.Services;
using KHost.Domain.Services;
using KHost.Domain.Services.Messaging;
using KHost.Domain.Services.Plugins;
using KHost.Abstractions.Messaging.Messages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;

namespace KHost.UnitTests.Domain.Services;

public class MediaAcquisitionServiceTests
{
    private readonly ILogger<MediaAcquisitionService> _logger = Substitute.For<ILogger<MediaAcquisitionService>>();
    private readonly IMediaRepository _repository = Substitute.For<IMediaRepository>();
    private readonly IMediaService _mediaService = Substitute.For<IMediaService>();
    private readonly IOptionsMonitor<MediaAcquisitionService.ServiceOptions> _options = Substitute.For<IOptionsMonitor<MediaAcquisitionService.ServiceOptions>>();
    private readonly DownloadsService _downloads = new(new MessageBroker(NullLogger<MessageBroker>.Instance));
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);
    private readonly MediaAcquisitionService _service;

    public MediaAcquisitionServiceTests()
    {
        _mediaService.CreateAsync(Arg.Any<Media>()).Returns(call => call.ArgAt<Media>(0));
        _options.CurrentValue.Returns(new MediaAcquisitionService.ServiceOptions());

        _service = new MediaAcquisitionService(_logger, _repository, _mediaService, _options, _downloads, _broker);
    }

    [Fact]
    public void MediaDirectory_ConfiguredValue_ReturnsItTrimmed()
    {
        _options.CurrentValue.Returns(new MediaAcquisitionService.ServiceOptions { MediaDirectory = "  /data/karaoke  " });

        Assert.Equal("/data/karaoke", _service.MediaDirectory);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MediaDirectory_BlankOrMissing_FallsBackToUserProfileKaraoke(string? configured)
    {
        _options.CurrentValue.Returns(new MediaAcquisitionService.ServiceOptions { MediaDirectory = configured });

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

    /// <summary>Two plugins' downloads and a host's folder can't be told apart by path alone.</summary>
    [Fact]
    public async Task ImportAsync_RequestNamesAProvider_RecordsItAsTheRowsSource()
    {
        _repository.FindByFilePathAsync(Arg.Any<string>()).Returns((Media?)null);
        var request = new MediaImportRequest
        {
            FilePath = "/downloads/song.mp4",
            Title = "Song Title",
            Source = "KaraFun",
        };

        await _service.ImportAsync(request);

        await _mediaService.Received(1).CreateAsync(Arg.Is<Media>(m => m.Source == "KaraFun"));
    }

    /// <summary>The download is registered before the file exists, and it is the same file.</summary>
    [Fact]
    public async Task BeginImportAsync_RequestNamesAProvider_RecordsItAsTheRowsSource()
    {
        _repository.FindByFilePathAsync(Arg.Any<string>()).Returns((Media?)null);
        var request = new MediaImportRequest
        {
            FilePath = "/downloads/song.mp4",
            Title = "Song Title",
            Source = "YouTube",
        };

        await _service.BeginImportAsync(request);

        await _mediaService.Received(1).CreateAsync(Arg.Is<Media>(m => m.Source == "YouTube"));
    }

    /// <summary>A file found on the host's own disk has no provider; inventing one is a lie.</summary>
    [Fact]
    public async Task ImportAsync_RequestNamesNoProvider_LeavesTheSourceEmpty()
    {
        _repository.FindByFilePathAsync(Arg.Any<string>()).Returns((Media?)null);
        var request = new MediaImportRequest { FilePath = "/karaoke/song.mp4", Title = "Song Title" };

        await _service.ImportAsync(request);

        await _mediaService.Received(1).CreateAsync(Arg.Is<Media>(m => m.Source == string.Empty));
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
    public async Task BeginProcessingAsync_MovesTheDownloadEntryToTheProcessingPhase()
    {
        var media = new Media { Id = Guid.NewGuid(), FilePath = "/downloads/song.kit", Title = "Song Title", Status = MediaStatus.Downloading };
        _repository.FindByFilePathAsync("/downloads/song.kit").Returns((Media?)null);
        _mediaService.CreateAsync(Arg.Any<Media>()).Returns(media);
        await _service.BeginImportAsync(new MediaImportRequest { FilePath = "/downloads/song.kit", Title = "Song Title" });
        _mediaService.ReadAsync(media.Id).Returns(media);

        await _service.BeginProcessingAsync(media.Id);

        // The row and the entry move together; the page reads the phase off the entry.
        Assert.Equal(DownloadPhase.Processing, _downloads.Snapshot().Single(d => d.MediaId == media.Id).Phase);
    }

    [Fact]
    public async Task FailImportAsync_WithAReason_PutsItOnTheDownloadEntry()
    {
        var media = new Media { Id = Guid.NewGuid(), FilePath = "/downloads/song.kit", Title = "Song Title", Status = MediaStatus.Downloading };
        _repository.FindByFilePathAsync("/downloads/song.kit").Returns((Media?)null);
        _mediaService.CreateAsync(Arg.Any<Media>()).Returns(media);
        await _service.BeginImportAsync(new MediaImportRequest { FilePath = "/downloads/song.kit", Title = "Song Title" });
        _mediaService.ReadAsync(media.Id).Returns(media);

        await _service.FailImportAsync(media.Id, "ffmpeg exited 1");

        Assert.Equal("ffmpeg exited 1", _downloads.Snapshot().Single(d => d.MediaId == media.Id).Reason);
    }

    [Fact]
    public async Task ReportDownloadProgressAsync_InBytes_ReachesTheDownloadEntry()
    {
        var media = new Media { Id = Guid.NewGuid(), FilePath = "/downloads/song.kit", Title = "Song Title", Status = MediaStatus.Downloading };
        _repository.FindByFilePathAsync("/downloads/song.kit").Returns((Media?)null);
        _mediaService.CreateAsync(Arg.Any<Media>()).Returns(media);
        await _service.BeginImportAsync(new MediaImportRequest { FilePath = "/downloads/song.kit", Title = "Song Title" });

        await _service.ReportDownloadProgressAsync(media.Id, 512L, 1024L);

        var entry = _downloads.Snapshot().Single(d => d.MediaId == media.Id);
        Assert.Equal(512L, entry.BytesReceived);
        Assert.Equal(0.5, entry.Progress);
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
        // A real MediaService, not the substitute: BaseRepositoryService.UpdateAsync is what
        // actually publishes MediaLibraryChanged, so a mock would only assert its own wiring.
        var repository = Substitute.For<IMediaRepository>();
        var mediaService = new MediaService(NullLogger<MediaService>.Instance, repository, _broker, new ServiceCollection().BuildServiceProvider());
        var media = new Media { Id = Guid.NewGuid(), FilePath = "/downloads/song.mp4", Title = "Song Title", Status = MediaStatus.Downloading };
        repository.ReadAsync(media.Id).Returns(media);

        var service = new MediaAcquisitionService(_logger, repository, mediaService, _options, _downloads, _broker);
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
        var mediaService = new MediaService(NullLogger<MediaService>.Instance, repository, _broker, new ServiceCollection().BuildServiceProvider());
        var media = new Media { Id = Guid.NewGuid(), FilePath = "/downloads/song.mp4", Title = "Song Title", Status = MediaStatus.Downloading };
        repository.ReadAsync(media.Id).Returns(media);

        var service = new MediaAcquisitionService(_logger, repository, mediaService, _options, _downloads, _broker);
        var raised = 0;
        using var subscription = _broker.Subscribe<MediaLibraryChanged>(_ => raised++);

        await service.FailImportAsync(media.Id);

        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task BeginProcessingAsync_DownloadingRow_SetsStatusToProcessing()
    {
        var media = new Media { Id = Guid.NewGuid(), FilePath = "/downloads/song.kit", Title = "Song Title", Status = MediaStatus.Downloading };
        _mediaService.ReadAsync(media.Id).Returns(media);

        await _service.BeginProcessingAsync(media.Id);

        await _mediaService.Received(1).UpdateAsync(Arg.Is<Media>(m => m.Id == media.Id && m.Status == MediaStatus.Processing));
    }

    [Fact]
    public async Task BeginProcessingAsync_DownloadingRow_LeavesTheDownloadEntryInFlight()
    {
        var media = new Media { Id = Guid.NewGuid(), FilePath = "/downloads/song.kit", Title = "Song Title", Status = MediaStatus.Downloading };
        _repository.FindByFilePathAsync("/downloads/song.kit").Returns((Media?)null);
        _mediaService.CreateAsync(Arg.Any<Media>()).Returns(media);
        await _service.BeginImportAsync(new MediaImportRequest { FilePath = "/downloads/song.kit", Title = "Song Title" });
        _mediaService.ReadAsync(media.Id).Returns(media);

        await _service.BeginProcessingAsync(media.Id);

        // Processing is the download's second phase, so the Downloads page must not settle yet.
        Assert.Equal(DownloadState.Downloading, _downloads.Snapshot().Single(d => d.MediaId == media.Id).State);
    }

    [Theory]
    [InlineData(MediaStatus.Ready)]
    [InlineData(MediaStatus.Broken)]
    public async Task BeginProcessingAsync_SettledRow_NeverDragsItBackIntoFlight(MediaStatus status)
    {
        var media = new Media { Id = Guid.NewGuid(), FilePath = "/downloads/song.kit", Title = "Song Title", Status = status };
        _mediaService.ReadAsync(media.Id).Returns(media);

        await _service.BeginProcessingAsync(media.Id);

        await _mediaService.DidNotReceive().UpdateAsync(Arg.Any<Media>());
    }

    [Fact]
    public async Task BeginProcessingAsync_NoSuchMedia_NoOps()
    {
        _mediaService.ReadAsync(Arg.Any<Guid>()).Returns((Media?)null);

        await _service.BeginProcessingAsync(Guid.NewGuid());

        await _mediaService.DidNotReceive().UpdateAsync(Arg.Any<Media>());
    }

    [Fact]
    public async Task BeginImportAsync_ExistingProcessingRow_StillReusesTheInFlightToken()
    {
        var media = new Media { Id = Guid.NewGuid(), FilePath = "/downloads/song.kit", Title = "Song Title", Status = MediaStatus.Downloading };
        _repository.FindByFilePathAsync("/downloads/song.kit").Returns((Media?)null);
        _mediaService.CreateAsync(Arg.Any<Media>()).Returns(media);
        var request = new MediaImportRequest { FilePath = "/downloads/song.kit", Title = "Song Title" };
        var first = await _service.BeginImportAsync(request);

        // Phase two is still in flight: a token of None here would leave the render uncancellable.
        media.Status = MediaStatus.Processing;
        _repository.FindByFilePathAsync("/downloads/song.kit").Returns(media);
        var second = await _service.BeginImportAsync(request);

        Assert.NotEqual(CancellationToken.None, second.Cancellation);

        await _downloads.CancelAsync(media.Id);

        Assert.True(first.Cancellation.IsCancellationRequested);
        Assert.True(second.Cancellation.IsCancellationRequested);
    }

    [Fact]
    public async Task DiscardImportAsync_ProcessingRowWhoseFileIsGone_DeletesIt()
    {
        // A cancelled render that cleaned up after itself is as discardable as a cancelled
        // download: what decides is the file, not which phase it reached.
        var media = new Media { Id = Guid.NewGuid(), FilePath = NoSuchPath(), Title = "Song Title", Status = MediaStatus.Processing };
        _mediaService.ReadAsync(media.Id).Returns(media);

        await _service.DiscardImportAsync(media.Id);

        await _mediaService.Received(1).DeleteAsync(media.Id);
    }

    [Theory]
    [InlineData(MediaStatus.Downloading)]
    [InlineData(MediaStatus.Processing)]
    public async Task DiscardImportAsync_FileOutlivedTheCancel_KeepsTheRowAsBroken(MediaStatus status)
    {
        // Whatever the caller believed it deleted, the file is still there, and a file with no
        // row pointing at it is one the folder scan imports again later as Ready.
        var file = NewTempFile();
        try
        {
            var media = new Media { Id = Guid.NewGuid(), FilePath = file, Title = "Song Title", Status = status };
            _mediaService.ReadAsync(media.Id).Returns(media);

            await _service.DiscardImportAsync(media.Id);

            await _mediaService.DidNotReceive().DeleteAsync(Arg.Any<Guid>());
            await _mediaService.Received(1).UpdateAsync(Arg.Is<Media>(m => m.Id == media.Id && m.Status == MediaStatus.Broken));
        }
        finally { File.Delete(file); }
    }

    private static string NoSuchPath()
        => Path.Combine(Path.GetTempPath(), $"khost-absent-{Guid.NewGuid():N}.khv");

    private static string NewTempFile()
    {
        var path = NoSuchPath();
        File.WriteAllText(path, "half a download");
        return path;
    }

    [Fact]
    public async Task DiscardImportAsync_DownloadingRow_DeletesIt()
    {
        var media = new Media { Id = Guid.NewGuid(), FilePath = NoSuchPath(), Title = "Song Title", Status = MediaStatus.Downloading };
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
        var mediaService = new MediaService(NullLogger<MediaService>.Instance, repository, _broker, new ServiceCollection().BuildServiceProvider());
        var media = new Media { Id = Guid.NewGuid(), FilePath = "/downloads/song.mp4", Title = "Song Title", Status = MediaStatus.Downloading };
        repository.ReadAsync(media.Id).Returns(media);
        repository.DeleteAsync(media.Id).Returns(true);

        var service = new MediaAcquisitionService(_logger, repository, mediaService, _options, _downloads, _broker);
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
        // Nothing to assert against but that it does not throw. DownloadsService itself already
        // covers the silent-no-op behaviour for an id it never registered.
        await _service.ReportDownloadProgressAsync(Guid.NewGuid(), 0.5);
    }
}
