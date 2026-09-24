using System.Reflection;
using KHost.Abstractions.Models;
using KHost.Abstractions.Models.Plugins;
using KHost.Abstractions.Repositories;
using KHost.Abstractions.Services;
using Microsoft.Extensions.Logging.Abstractions;
using KHost.Domain.Services;
using KHost.Domain.Services.Messaging;

namespace KHost.UnitTests.Domain.Services;

public class MediaImportServiceTests
{
    private readonly IMediaFileParsingService _parser = Substitute.For<IMediaFileParsingService>();
    private readonly IMediaRepository _repository = Substitute.For<IMediaRepository>();
    private readonly IMediaService _mediaService = Substitute.For<IMediaService>();
    private readonly IMediaFingerprintService _fingerprints = Substitute.For<IMediaFingerprintService>();
    private readonly IAnalyticsService _analytics = Substitute.For<IAnalyticsService>();
    private readonly IPluginRegistry _plugins = Substitute.For<IPluginRegistry>();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);
    private readonly MediaImportService _service;

    public MediaImportServiceTests()
    {
        _plugins.Plugins.Returns((IReadOnlyList<DiscoveredPlugin>)[]);

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
            _analytics,
            _plugins,
            _broker);
    }

    [Fact]
    public async Task StartAsync_DoesNothing_WhenPathListIsEmpty()
    {
        await _service.StartAsync([]);

        Assert.Equal(ImportState.Idle, _service.State);
        await _repository.DidNotReceive().GetExistingFilePathsAsync(Arg.Any<IEnumerable<string>>());
    }

    /// <summary>Mistyping a still or ad as karaoke gives it a fallback artist in song search.</summary>
    [Theory]
    [InlineData("/room/venue-card.jpg", MediaType.Image)]
    [InlineData("/room/free-fallin.mp3", MediaType.Audio)]
    [InlineData("/room/livin-on-a-prayer.mp4", MediaType.Karaoke)]
    public async Task StartAsync_ImportsEachFileAsWhatItIs(string path, MediaType expected)
    {
        _parser.LoadAndParseAsync(Arg.Any<string>(), Arg.Any<MediaType>())
            .Returns(new Media { FilePath = path, Title = "A" });

        await _service.StartAsync([path]);
        await WaitForIdleAsync();

        await _parser.Received(1).LoadAndParseAsync(path, expected);
    }

    /// <summary>Video and ad share formats; a per-file answer beats the name-derived one.</summary>
    [Fact]
    public async Task StartAsync_TheHostAnsweredForThisFile_UsesThatRatherThanTheBatchAnswer()
    {
        _parser.LoadAndParseAsync(Arg.Any<string>(), Arg.Any<MediaType>())
            .Returns(new Media { FilePath = "/room/spot.mp4", Title = "A" });

        _service.VideoIsKaraoke = true;
        _service.TypeOverrides["/room/spot.mp4"] = MediaType.Video;

        await _service.StartAsync(["/room/spot.mp4"]);
        await WaitForIdleAsync();

        await _parser.Received(1).LoadAndParseAsync("/room/spot.mp4", MediaType.Video);
    }

    /// <summary>The batch answer still decides every file the host did not speak for.</summary>
    [Fact]
    public async Task StartAsync_TheHostSaidTheseAreAds_TypesVideoThatWay()
    {
        _parser.LoadAndParseAsync(Arg.Any<string>(), Arg.Any<MediaType>())
            .Returns(new Media { FilePath = "/room/spot.mp4", Title = "A" });

        _service.VideoIsKaraoke = false;

        await _service.StartAsync(["/room/spot.mp4"]);
        await WaitForIdleAsync();

        await _parser.Received(1).LoadAndParseAsync("/room/spot.mp4", MediaType.Video);
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

    /// <summary>The bug this exists for: the finally block used to set Idle, then dispose and null
    /// the field unconditionally, so a StartAsync that raced into the gap had its brand-new CTS
    /// disposed and its own field entry wiped out from under it. The field is poked directly to
    /// stand in for that race, since the real one is a same-instant multi-thread race with no seam
    /// to trigger deterministically.</summary>
    [Fact]
    public async Task RunImportAsync_LeavesANewerCts_WhenAStartAsyncRacedTheCleanup()
    {
        var tcs = new TaskCompletionSource<Media>();
        _parser.LoadAndParseAsync(Arg.Any<string>()).Returns(_ => tcs.Task);

        await _service.StartAsync(["/a.mp4"]);

        var ctsField = typeof(MediaImportService)
            .GetField("_cts", BindingFlags.NonPublic | BindingFlags.Instance)!;
        using var impostor = new CancellationTokenSource();
        ctsField.SetValue(_service, impostor);

        tcs.SetResult(new Media { FilePath = "/a.mp4", Title = "A" });
        await WaitForIdleAsync();

        // Left alone: not disposed (Cancel would throw ObjectDisposedException otherwise), and
        // not nulled out from under whichever run actually owns it.
        Assert.Same(impostor, ctsField.GetValue(_service));
        impostor.Cancel();
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

    [Fact]
    public void SupportedExtensions_IncludeTheBuiltInFormats()
        => Assert.Superset(new HashSet<string> { ".mp4", ".mkv", ".cdg" }, Build().SupportedExtensions.ToHashSet());

    [Fact]
    public void SupportedExtensions_IncludeALoadedPluginsImportFormats()
    {
        var service = Build(Plugin(PluginStatus.Loaded, ".khv"));

        Assert.Contains(".khv", service.SupportedExtensions);
    }

    [Fact]
    public void SupportedExtensions_ExcludeAnUnloadedPluginsImportFormats()
    {
        var service = Build(Plugin(PluginStatus.Disabled, ".khv"));

        Assert.DoesNotContain(".khv", service.SupportedExtensions);
    }

    /// <summary>A plugin may hand back any casing, with or without a dot; all one extension.</summary>
    [Fact]
    public void SupportedExtensions_NormalizeAndDeduplicatePluginFormats()
    {
        var service = Build(Plugin(PluginStatus.Loaded, "KHV", "*.khv", ".Khv"));

        // Three spellings collapse to exactly one entry, lower-cased with a leading dot.
        var khv = service.SupportedExtensions
            .Where(extension => extension.Equals(".khv", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Equal([".khv"], khv);
    }

    private MediaImportService Build(params DiscoveredPlugin[] plugins)
    {
        var registry = Substitute.For<IPluginRegistry>();
        registry.Plugins.Returns(plugins);
        return new MediaImportService(
            NullLogger<MediaImportService>.Instance, _parser, _repository, _mediaService,
            _fingerprints, _analytics, registry, _broker);
    }

    private static DiscoveredPlugin Plugin(PluginStatus status, params string[] importFormats) => new()
    {
        Directory = "/plugins/x",
        Status = status,
        Manifest = new PluginManifest
        {
            Id = Guid.NewGuid(),
            Name = "X",
            Version = "1.0.0",
            EntryAssembly = "X.dll",
            ApiVersion = PluginApi.CurrentVersion,
            ImportFormats = [.. importFormats],
        },
    };
}
