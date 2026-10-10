using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.Abstractions.Services;
using KHost.Domain.Services;
using KHost.Domain.Services.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KHost.UnitTests.Domain.Services;

/// <summary>Rows a provider marked ephemeral or single-use: their flags, and how their file going
/// and coming back moves their status.</summary>
public class MediaAcquisitionServiceFileLifetimeTests : IDisposable
{
    private readonly IMediaRepository _repository = Substitute.For<IMediaRepository>();
    private readonly IMediaService _mediaService = Substitute.For<IMediaService>();
    private readonly IOptionsMonitor<MediaAcquisitionService.ServiceOptions> _options = Substitute.For<IOptionsMonitor<MediaAcquisitionService.ServiceOptions>>();
    private readonly DownloadsService _downloads = new(new MessageBroker(NullLogger<MessageBroker>.Instance));
    private readonly MediaAcquisitionService _service;
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"khost-lifetime-{Guid.NewGuid():N}");

    public MediaAcquisitionServiceFileLifetimeTests()
    {
        Directory.CreateDirectory(_directory);
        _mediaService.CreateAsync(Arg.Any<Media>()).Returns(call => call.ArgAt<Media>(0));
        _options.CurrentValue.Returns(new MediaAcquisitionService.ServiceOptions());

        _service = new MediaAcquisitionService(
            NullLogger<MediaAcquisitionService>.Instance, _repository, _mediaService, _options, _downloads,
            new MessageBroker(NullLogger<MessageBroker>.Instance));
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
        GC.SuppressFinalize(this);
    }

    private string MissingFile() => Path.Combine(_directory, $"{Guid.NewGuid():N}.mp4");

    private string PresentFile()
    {
        var path = MissingFile();
        File.WriteAllBytes(path, [1]);
        return path;
    }

    private Media Row(string path, MediaStatus status, bool ephemeral = true, bool singleUse = false)
    {
        var media = new Media { Id = Guid.NewGuid(), FilePath = path, Title = "Africa", Source = "YouTube", Status = status, IsEphemeral = ephemeral, IsSingleUse = singleUse };
        _repository.FindByFilePathAsync(path).Returns(media);
        _mediaService.ReadAsync(media.Id).Returns(media);
        return media;
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task ImportAsync_CarriesTheProvidersFlagsAndKey(bool ephemeral, bool singleUse)
    {
        await _service.ImportAsync(new MediaImportRequest
        {
            FilePath = MissingFile(), Title = "Africa", SourceKey = "abc123", IsEphemeral = ephemeral, IsSingleUse = singleUse,
        });

        await _mediaService.Received(1).CreateAsync(Arg.Is<Media>(m =>
            m.SourceKey == "abc123" && m.IsEphemeral == ephemeral && m.IsSingleUse == singleUse));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task BeginImportAsync_CarriesTheProvidersFlagsAndKey(bool ephemeral, bool singleUse)
    {
        await _service.BeginImportAsync(new MediaImportRequest
        {
            FilePath = MissingFile(), Title = "Africa", SourceKey = "abc123", IsEphemeral = ephemeral, IsSingleUse = singleUse,
        });

        await _mediaService.Received(1).CreateAsync(Arg.Is<Media>(m =>
            m.SourceKey == "abc123" && m.IsEphemeral == ephemeral && m.IsSingleUse == singleUse));
    }

    /// <summary>A provider fetching a removed file through its usual path must see it in flight, with
    /// a Downloads entry the host can cancel, not a settled row it would race.</summary>
    [Fact]
    public async Task BeginImportAsync_ARowWaitingForItsFile_MovesItToDownloadingWithACancellableEntry()
    {
        var media = Row(MissingFile(), MediaStatus.NotDownloaded);

        var ticket = await _service.BeginImportAsync(new MediaImportRequest { FilePath = media.FilePath, Title = "Africa" });

        Assert.Equal(media.Id, ticket.MediaId);
        Assert.Equal(MediaStatus.Downloading, media.Status);
        await _mediaService.Received(1).UpdateAsync(media);
        Assert.True(ticket.Cancellation.CanBeCanceled);
        await _mediaService.DidNotReceive().CreateAsync(Arg.Any<Media>());
    }

    [Theory]
    [InlineData(MediaStatus.Ready)]
    [InlineData(MediaStatus.Broken)]
    public async Task BeginRefetchAsync_ARowNotWaitingForItsFile_LeavesItAlone(MediaStatus status)
    {
        var media = Row(PresentFile(), status);

        var ticket = await _service.BeginRefetchAsync(media);

        Assert.Equal(status, media.Status);
        Assert.False(ticket.Cancellation.CanBeCanceled);
        await _mediaService.DidNotReceive().UpdateAsync(Arg.Any<Media>());
    }

    /// <summary>The row has history behind it, so a cancelled refetch puts it back to wait rather than
    /// deleting it as a cancelled first download is.</summary>
    [Fact]
    public async Task DiscardImportAsync_ACancelledRefetch_KeepsTheRowAsNotDownloaded()
    {
        var media = Row(MissingFile(), MediaStatus.NotDownloaded);
        await _service.BeginRefetchAsync(media);

        await _service.DiscardImportAsync(media.Id);

        Assert.Equal(MediaStatus.NotDownloaded, media.Status);
        await _mediaService.DidNotReceive().DeleteAsync(media.Id);
    }

    [Fact]
    public async Task DiscardImportAsync_ACancelledFirstDownload_StillDeletesTheRow()
    {
        var media = Row(MissingFile(), MediaStatus.Downloading);

        await _service.DiscardImportAsync(media.Id);

        await _mediaService.Received(1).DeleteAsync(media.Id);
    }

    /// <summary>Broken waits for a host to mend it; a marked row with no file is retried on its next queue instead.</summary>
    [Theory]
    [InlineData(true, false, false, MediaStatus.NotDownloaded)]
    [InlineData(false, true, false, MediaStatus.NotDownloaded)]
    [InlineData(true, false, true, MediaStatus.Broken)]
    [InlineData(false, false, false, MediaStatus.Broken)]
    public async Task FailImportAsync_SettlesAMarkedRowWithNoFileAsNotDownloaded(bool ephemeral, bool singleUse, bool fileThere, MediaStatus expected)
    {
        var media = Row(fileThere ? PresentFile() : MissingFile(), MediaStatus.Downloading, ephemeral, singleUse);

        await _service.FailImportAsync(media.Id, "network");

        Assert.Equal(expected, media.Status);
    }

    [Fact]
    public async Task ImportAsync_ARowWaitingForAFileThatIsBack_MakesItReady()
    {
        var media = Row(PresentFile(), MediaStatus.NotDownloaded);

        var id = await _service.ImportAsync(new MediaImportRequest { FilePath = media.FilePath, Title = "Africa" });

        Assert.Equal(media.Id, id);
        Assert.Equal(MediaStatus.Ready, media.Status);
    }

    [Fact]
    public async Task ImportAsync_ARowWaitingForAFileStillMissing_LeavesItWaiting()
    {
        var media = Row(MissingFile(), MediaStatus.NotDownloaded);

        await _service.ImportAsync(new MediaImportRequest { FilePath = media.FilePath, Title = "Africa" });

        Assert.Equal(MediaStatus.NotDownloaded, media.Status);
    }
}
