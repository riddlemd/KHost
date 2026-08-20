using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.Abstractions.Services;
using KHost.Domain.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.Domain.Services;

public class MediaImportServiceTests
{
    private readonly IMediaFileParsingService _parser = Substitute.For<IMediaFileParsingService>();
    private readonly IMediaRepository _repository = Substitute.For<IMediaRepository>();
    private readonly IMediaService _mediaService = Substitute.For<IMediaService>();
    private readonly IMediaFingerprintService _fingerprints = Substitute.For<IMediaFingerprintService>();
    private readonly IAnalyticsService _analytics = Substitute.For<IAnalyticsService>();
    private readonly MediaImportService _service;

    public MediaImportServiceTests()
    {
        _repository.GetExistingFilePathsAsync(Arg.Any<IEnumerable<string>>())
            .Returns(new HashSet<string>());
        _repository.GetByFileSizesAsync(Arg.Any<IEnumerable<long>>())
            .Returns((IReadOnlyList<Media>)[]);
        _repository.GetWithoutFileSizeAsync()
            .Returns((IReadOnlyList<Media>)[]);
        _analytics.StartActivity(Arg.Any<string>())
            .Returns(Substitute.For<IAnalyticsActivity>());
        _service = new MediaImportService(
            NullLogger<MediaImportService>.Instance,
            _parser,
            _repository,
            _mediaService,
            _fingerprints,
            _analytics);
    }

    [Fact]
    public async Task StartAsync_DoesNothing_WhenPathListIsEmpty()
    {
        await _service.StartAsync([]);

        Assert.Equal(ImportState.Idle, _service.State);
        await _repository.DidNotReceive().GetExistingFilePathsAsync(Arg.Any<IEnumerable<string>>());
    }

    [Fact]
    public async Task StartAsync_SetsRunningState_ThenIdleWhenDone()
    {
        _parser.LoadAndParseAsync(Arg.Any<string>())
            .Returns(new Media { FilePath = "/a.mp4", Title = "A" });

        await _service.StartAsync(["/a.mp4"]);
        await WaitForIdleAsync();

        Assert.Equal(ImportState.Idle, _service.State);
    }

    [Fact]
    public async Task Cancel_SetsCancellingState()
    {
        var tcs = new TaskCompletionSource<Media>();
        _parser.LoadAndParseAsync(Arg.Any<string>()).Returns(_ => tcs.Task);

        await _service.StartAsync(["/a.mp4", "/b.mp4"]);

        _service.Cancel();

        Assert.Equal(ImportState.Cancelling, _service.State);
        tcs.SetResult(new Media { FilePath = "/a.mp4", Title = "A" });
        await WaitForIdleAsync();
    }

    [Fact]
    public void Cancel_DoesNothing_WhenNotRunning()
    {
        _service.Cancel();

        Assert.Equal(ImportState.Idle, _service.State);
    }

    [Fact]
    public async Task RunImportAsync_ImportsAllFiles_WhenNoExistingFiles()
    {
        var paths = new List<string> { "/a.mp4", "/b.mp4" };
        _parser.LoadAndParseAsync(Arg.Any<string>())
            .Returns(args => new Media { FilePath = (string)args[0], Title = "T" });

        await _service.StartAsync(paths);
        await WaitForIdleAsync();

        Assert.Equal(2, _service.ImportedCount);
        Assert.Equal(0, _service.FailedCount);
    }

    [Fact]
    public async Task RunImportAsync_SkipsExistingPaths()
    {
        var paths = new List<string> { "/a.mp4", "/b.mp4" };
        _repository.GetExistingFilePathsAsync(Arg.Any<IEnumerable<string>>())
            .Returns(new HashSet<string> { "/a.mp4" });
        _parser.LoadAndParseAsync("/b.mp4")
            .Returns(new Media { FilePath = "/b.mp4", Title = "B" });

        await _service.StartAsync(paths);
        await WaitForIdleAsync();

        Assert.Equal(1, _service.ImportedCount);
        await _parser.DidNotReceive().LoadAndParseAsync("/a.mp4");
    }

    [Fact]
    public async Task RunImportAsync_ContinuesAfterIndividualFileFailure()
    {
        var paths = new List<string> { "/bad.mp4", "/good.mp4" };
        _parser.LoadAndParseAsync("/bad.mp4")
            .Returns(Task.FromException<Media>(new InvalidOperationException("parse failed")));
        _parser.LoadAndParseAsync("/good.mp4")
            .Returns(new Media { FilePath = "/good.mp4", Title = "Good" });

        await _service.StartAsync(paths);
        await WaitForIdleAsync();

        Assert.Equal(1, _service.ImportedCount);
        Assert.Equal(1, _service.FailedCount);
        Assert.Equal(ImportState.Idle, _service.State);
    }

    [Fact]
    public async Task RunImportAsync_SkipsAFileWhoseBytesAreAlreadyInTheLibrary()
    {
        GivenLibrary(Row("/library/song.mp4", size: 100, sampled: "s", full: "f"));
        GivenFile("/new/copy.mp4", size: 100, sampled: "s", full: "f");

        await ImportAsync("/new/copy.mp4");

        Assert.Equal(0, _service.ImportedCount);
        await _parser.DidNotReceive().LoadAndParseAsync("/new/copy.mp4");
    }

    [Fact]
    public async Task RunImportAsync_ImportsAFile_WhenTheSampledHashCollidesButTheBytesDiffer()
    {
        GivenLibrary(Row("/library/song.mp4", size: 100, sampled: "s", full: "f"));
        GivenFile("/new/other.mp4", size: 100, sampled: "s", full: "DIFFERENT");
        GivenParsed("/new/other.mp4");

        await ImportAsync("/new/other.mp4");

        Assert.Equal(1, _service.ImportedCount);
    }

    [Fact]
    public async Task RunImportAsync_ImportsAFile_WhenNoLibraryRowSharesItsSize()
    {
        GivenLibrary(Row("/library/song.mp4", size: 100, sampled: "s", full: "f"));
        GivenFile("/new/other.mp4", size: 999, sampled: "s", full: "f");
        GivenParsed("/new/other.mp4");

        await ImportAsync("/new/other.mp4");

        Assert.Equal(1, _service.ImportedCount);

        // The size prefilter answered it, so the expensive tier never ran on either file.
        await _fingerprints.DidNotReceive().ComputeFullHashAsync("/new/other.mp4", Arg.Any<CancellationToken>());
        await _fingerprints.DidNotReceive().ComputeSampledHashAsync("/library/song.mp4", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunImportAsync_SkipsTheSecondOfTwoIdenticalFilesInOneSelection()
    {
        GivenFile("/new/a.mp4", size: 100, sampled: "s", full: "f");
        GivenFile("/new/b.mp4", size: 100, sampled: "s", full: "f");
        GivenParsed("/new/a.mp4");

        await ImportAsync("/new/a.mp4", "/new/b.mp4");

        Assert.Equal(1, _service.ImportedCount);
        await _parser.DidNotReceive().LoadAndParseAsync("/new/b.mp4");
    }

    [Fact]
    public async Task RunImportAsync_StampsTheSizeAndSampledHashOnTheImportedRow()
    {
        GivenFile("/new/song.mp4", size: 100, sampled: "s", full: "f");
        GivenParsed("/new/song.mp4");

        await ImportAsync("/new/song.mp4");

        await _mediaService.Received(1).CreateAsync(
            Arg.Is<Media>(m => m.FileSize == 100 && m.SampledHash == "s"));
    }

    [Fact]
    public async Task RunImportAsync_MeasuresLibraryRowsThatHaveNoSizeYet()
    {
        var unmeasured = Row("/library/old.mp4", size: null, sampled: null, full: null);
        _repository.GetWithoutFileSizeAsync().Returns((IReadOnlyList<Media>)[unmeasured]);
        _fingerprints.TryGetSize("/library/old.mp4").Returns(512);
        GivenFile("/new/song.mp4", size: 100, sampled: "s", full: "f");
        GivenParsed("/new/song.mp4");

        await ImportAsync("/new/song.mp4");

        Assert.Equal(512, unmeasured.FileSize);
        await _repository.Received().UpdateFingerprintsAsync(
            Arg.Is<IEnumerable<Media>>(rows => rows.Contains(unmeasured)));
    }

    [Fact]
    public async Task RunImportAsync_PersistsAHashItHadToComputeForAnExistingRow()
    {
        var row = Row("/library/song.mp4", size: 100, sampled: null, full: null);
        GivenLibrary(row);
        GivenFile("/library/song.mp4", size: 100, sampled: "s", full: "f");
        GivenFile("/new/copy.mp4", size: 100, sampled: "s", full: "f");

        await ImportAsync("/new/copy.mp4");

        Assert.Equal("s", row.SampledHash);
        Assert.Equal("f", row.ContentHash);
        await _repository.Received().UpdateFingerprintsAsync(
            Arg.Is<IEnumerable<Media>>(rows => rows.Contains(row)));
    }

    [Fact]
    public async Task RunImportAsync_ImportsAFileItCannotMeasure()
    {
        GivenLibrary(Row("/library/song.mp4", size: 100, sampled: "s", full: "f"));
        _fingerprints.TryGetSize("/new/unreadable.mp4").Returns((long?)null);
        GivenParsed("/new/unreadable.mp4");

        await ImportAsync("/new/unreadable.mp4");

        Assert.Equal(1, _service.ImportedCount);
    }

    private static Media Row(string path, long? size, string? sampled, string? full)
        => new() { FilePath = path, Title = "T", FileSize = size, SampledHash = sampled, ContentHash = full };

    private void GivenLibrary(params Media[] rows)
        => _repository.GetByFileSizesAsync(Arg.Any<IEnumerable<long>>())
            .Returns(call => (IReadOnlyList<Media>)rows
                .Where(r => r.FileSize is not null && call.Arg<IEnumerable<long>>().Contains(r.FileSize!.Value))
                .ToList());

    private void GivenFile(string path, long size, string sampled, string full)
    {
        _fingerprints.TryGetSize(path).Returns(size);
        _fingerprints.ComputeSampledHashAsync(path, Arg.Any<CancellationToken>()).Returns(sampled);
        _fingerprints.ComputeFullHashAsync(path, Arg.Any<CancellationToken>()).Returns(full);
    }

    private void GivenParsed(string path)
        => _parser.LoadAndParseAsync(path).Returns(_ => new Media { FilePath = path, Title = "T" });

    private async Task ImportAsync(params string[] paths)
    {
        await _service.StartAsync(paths);
        await WaitForIdleAsync();
    }

    private async Task WaitForIdleAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (_service.State != ImportState.Idle && DateTime.UtcNow < deadline)
            await Task.Delay(50);
        Assert.Equal(ImportState.Idle, _service.State);
    }
}
