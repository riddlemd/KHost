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
    private readonly IAnalyticsService _analytics = Substitute.For<IAnalyticsService>();
    private readonly MediaImportService _service;

    public MediaImportServiceTests()
    {
        _repository.GetExistingFilePathsAsync(Arg.Any<IEnumerable<string>>())
            .Returns(new HashSet<string>());
        _analytics.StartActivity(Arg.Any<string>())
            .Returns(Substitute.For<IAnalyticsActivity>());
        _service = new MediaImportService(
            NullLogger<MediaImportService>.Instance,
            _parser,
            _repository,
            _mediaService,
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

    private async Task WaitForIdleAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (_service.State != ImportState.Idle && DateTime.UtcNow < deadline)
            await Task.Delay(50);
        Assert.Equal(ImportState.Idle, _service.State);
    }
}
