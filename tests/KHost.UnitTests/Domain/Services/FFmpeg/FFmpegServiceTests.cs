using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using FFMpegCore;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services;
using KHost.Domain.Services.FFmpeg;
using KHost.Domain.Services.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.Domain.Services.FFmpeg;

/// <summary>Programs here are text files: the fake runner reads "runs:&lt;version&gt;" as a program
/// that answers, and anything else as one this machine refuses.</summary>
public sealed class FFmpegServiceTests : IDisposable
{
    private const string Url = "https://builds.example/ffmpeg.zip";

    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("khost-ffmpeg-");
    private readonly IMessageBroker _broker = new MessageBroker(NullLogger<MessageBroker>.Instance);
    private readonly string _savedBinaryFolder = GlobalFFOptions.Current.BinaryFolder;
    private readonly Dictionary<string, string?> _configuration = [];

    private string Bin => Path.Combine(_root.FullName, "bin");
    private string OnPath => Directory.CreateDirectory(Path.Combine(_root.FullName, "path")).FullName;

    public void Dispose()
    {
        GlobalFFOptions.Configure(options => options.BinaryFolder = _savedBinaryFolder);

        try { _root.Delete(recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void HostDirectories_ByDefault_IsABinFolderBesideTheHost()
        => Assert.Equal(Path.Combine(AppContext.BaseDirectory, "bin"), new HostDirectories().BinDirectory);

    /// <summary>What a plugin reaches through IHostDirectories is where the host looks, ahead of PATH.</summary>
    [Fact]
    public void Locate_ProgramInTheSharedBinFolder_WinsOverPath()
    {
        Place(OnPath, "ffmpeg", "runs:8.0");
        Place(Bin, "ffmpeg", "runs:9.0");

        Assert.Equal(Path.Combine(Bin, "ffmpeg"), Service().Locate(FFmpegTool.FFmpeg));
    }

    [Fact]
    public void Locate_ConfiguredFolder_WinsOverTheBinFolder()
    {
        var configured = Directory.CreateDirectory(Path.Combine(_root.FullName, "configured")).FullName;
        Place(configured, "ffprobe", "runs:7.1");
        Place(Bin, "ffprobe", "runs:9.0");
        _configuration[FFmpegService.ConfigurationKey] = configured;

        Assert.Equal(Path.Combine(configured, "ffprobe"), Service().Locate(FFmpegTool.FFprobe));
    }

    [Fact]
    public async Task CheckAsync_BothFound_ReportsEachVersionAndWhere()
    {
        Place(Bin, "ffmpeg", "runs:9.0");
        Place(OnPath, "ffprobe", "runs:8.1");

        var status = await Service().CheckAsync();

        Assert.True(status.IsReady);
        Assert.True(status.HasChecked);
        Assert.Equal(("9.0", Path.Combine(Bin, "ffmpeg")), (status.FFmpeg.Version, status.FFmpeg.Path));
        Assert.Equal(("8.1", Path.Combine(OnPath, "ffprobe")), (status.FFprobe.Version, status.FFprobe.Path));
    }

    [Fact]
    public async Task CheckAsync_NeitherFound_ReportsBothMissing()
    {
        var status = await Service().CheckAsync();

        Assert.False(status.IsReady);
        Assert.Null(status.FFmpeg.Path);
        Assert.Null(status.FFprobe.Path);
    }

    [Fact]
    public async Task CheckAsync_FoundButRefusedToRun_SaysWhy()
    {
        Place(Bin, "ffmpeg", "unsigned");
        Place(Bin, "ffprobe", "runs:9.0");

        var status = await Service().CheckAsync();

        Assert.False(status.FFmpeg.IsUsable);
        Assert.Equal(Path.Combine(Bin, "ffmpeg"), status.FFmpeg.Path);
        Assert.Equal("refused", status.FFmpeg.Error);
    }

    [Fact]
    public async Task CheckAsync_PointsFFMpegCoreAtTheFfprobeItFound()
    {
        Place(Bin, "ffprobe", "runs:9.0");

        await Service().CheckAsync();

        Assert.Equal(Bin, GlobalFFOptions.Current.BinaryFolder);
    }

    [Fact]
    public async Task CheckAsync_AnnouncesAChange_AndNotARepeat()
    {
        Place(Bin, "ffmpeg", "runs:9.0");
        var service = Service();
        var raised = 0;
        using var subscription = _broker.Subscribe<FFmpegChanged>(_ => raised++);

        await service.CheckAsync();
        await service.CheckAsync();

        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task InstallAsync_PinnedBuild_PutsBothProgramsInBinAndUsesThemLive()
    {
        var zip = Zip(("pkg/bin/ffmpeg", "runs:9.0"), ("pkg/bin/ffprobe", "runs:9.0"));
        var service = Service(zip, Build(zip));

        var status = await service.InstallAsync();

        Assert.Equal(FFmpegInstallState.Succeeded, status.Install.State);
        Assert.True(status.IsReady);
        Assert.Equal(Path.Combine(Bin, "ffmpeg"), status.FFmpeg.Path);
        Assert.Equal(Path.Combine(Bin, "ffprobe"), status.FFprobe.Path);
        Assert.Equal(Bin, GlobalFFOptions.Current.BinaryFolder);
        Assert.True(File.Exists(Path.Combine(Bin, FFmpegService.MarkerFileName)));
        Assert.False(Directory.Exists(Path.Combine(Bin, FFmpegService.WorkFolderName)));

        if (!OperatingSystem.IsWindows())
            Assert.True(File.GetUnixFileMode(Path.Combine(Bin, "ffmpeg")).HasFlag(UnixFileMode.UserExecute));
    }

    /// <summary>The archive here would escape if opened; the error proves it never was.</summary>
    [Fact]
    public async Task InstallAsync_ChecksumDoesNotMatch_RefusesBeforeOpeningTheArchive()
    {
        var zip = Zip(("../../escaped", "x"), ("ffmpeg", "runs:9.0"), ("ffprobe", "runs:9.0"));
        var service = Service(zip, Build(zip, sha256: new string('0', 64)));

        var status = await service.InstallAsync();

        Assert.Equal(FFmpegInstallState.Failed, status.Install.State);
        Assert.Contains("checksum", status.Install.Error);
        Assert.False(File.Exists(Path.Combine(Bin, "ffmpeg")));
    }

    [Fact]
    public async Task InstallAsync_AnEntryEscapesItsFolder_IsRefusedAndNothingIsWritten()
    {
        var zip = Zip(("ffmpeg", "runs:9.0"), ("ffprobe", "runs:9.0"), ("../../escaped", "x"));
        var service = Service(zip, Build(zip, files: new() { ["ffmpeg"] = "ffmpeg", ["ffprobe"] = "ffprobe" }));

        var status = await service.InstallAsync();

        Assert.Equal(FFmpegInstallState.Failed, status.Install.State);
        Assert.Contains("outside its folder", status.Install.Error);
        Assert.False(File.Exists(Path.Combine(Bin, "ffmpeg")));
        Assert.Empty(Directory.GetFiles(_root.FullName, "escaped", SearchOption.AllDirectories));
    }

    /// <summary>A build this machine will not run must not displace the one that worked.</summary>
    [Fact]
    public async Task InstallAsync_DownloadedProgramDoesNotRun_LeavesTheInstalledOneInPlace()
    {
        Place(Bin, "ffmpeg", "runs:8.0");
        var zip = Zip(("pkg/bin/ffmpeg", "unsigned"), ("pkg/bin/ffprobe", "runs:9.0"));
        var service = Service(zip, Build(zip));

        var status = await service.InstallAsync();

        Assert.Equal(FFmpegInstallState.Failed, status.Install.State);
        Assert.Contains("does not run on this computer", status.Install.Error);
        Assert.Equal("runs:8.0", File.ReadAllText(Path.Combine(Bin, "ffmpeg")));
    }

    [Fact]
    public async Task InstallAsync_NoBuildPinnedForThisPlatform_SaysSo()
    {
        var zip = Zip(("pkg/bin/ffmpeg", "runs:9.0"), ("pkg/bin/ffprobe", "runs:9.0"));
        var service = Service(zip, Build(zip), architecture: "riscv64");

        Assert.False(service.CanInstall);

        var status = await service.InstallAsync();

        Assert.Equal(FFmpegInstallState.Failed, status.Install.State);
        Assert.Contains("no FFmpeg download for this computer", status.Install.Error);
    }

    [Fact]
    public async Task InstallAsync_DownloadRunsPastThePinnedSize_IsStopped()
    {
        var zip = Zip(("pkg/bin/ffmpeg", "runs:9.0"), ("pkg/bin/ffprobe", "runs:9.0"));
        var service = Service(zip, Build(zip, size: zip.Length - 1));

        var status = await service.InstallAsync();

        Assert.Equal(FFmpegInstallState.Failed, status.Install.State);
        Assert.Contains("larger than the file KHost pinned", status.Install.Error);
    }

    [Fact]
    public async Task InstallAsync_ArchiveLacksAProgram_Fails()
    {
        var zip = Zip(("pkg/bin/ffmpeg", "runs:9.0"));
        var service = Service(zip, Build(zip));

        var status = await service.InstallAsync();

        Assert.Equal(FFmpegInstallState.Failed, status.Install.State);
        Assert.Contains("pkg/bin/ffprobe", status.Install.Error);
    }

    [Fact]
    public async Task InstallAsync_ManifestUrlIsNotHttps_IsRefused()
    {
        var zip = Zip(("pkg/bin/ffmpeg", "runs:9.0"), ("pkg/bin/ffprobe", "runs:9.0"));
        var service = Service(zip, Build(zip, url: "http://builds.example/ffmpeg.zip"));

        var status = await service.InstallAsync();

        Assert.Equal(FFmpegInstallState.Failed, status.Install.State);
        Assert.Contains("not an https address", status.Install.Error);
    }

    private const string Unreachable = "Could not reach the FFmpeg download site. Check this computer is online, then try again.";

    /// <summary>HttpClient's own timeout arrives as a cancellation nobody asked for.</summary>
    [Fact]
    public async Task InstallAsync_DownloadTimesOut_SaysTheSiteCouldNotBeReached()
    {
        var zip = Zip(("pkg/bin/ffmpeg", "runs:9.0"), ("pkg/bin/ffprobe", "runs:9.0"));
        var timeout = new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout", new TimeoutException());
        var service = Service(build: Build(zip), handler: () => new ThrowingHandler(timeout));

        var status = await service.InstallAsync();

        Assert.Equal(FFmpegInstallState.Failed, status.Install.State);
        Assert.Equal(Unreachable, status.Install.Error);
    }

    [Fact]
    public async Task InstallAsync_ConnectionDropsMidDownload_SaysTheSiteCouldNotBeReached()
    {
        var zip = Zip(("pkg/bin/ffmpeg", "runs:9.0"), ("pkg/bin/ffprobe", "runs:9.0"));
        var service = Service(build: Build(zip), handler: () => new DroppingHandler());

        var status = await service.InstallAsync();

        Assert.Equal(FFmpegInstallState.Failed, status.Install.State);
        Assert.Equal(Unreachable, status.Install.Error);
    }

    [Fact]
    public async Task InstallAsync_CallerCancels_SaysItWasCancelled()
    {
        var zip = Zip(("pkg/bin/ffmpeg", "runs:9.0"), ("pkg/bin/ffprobe", "runs:9.0"));
        var service = Service(build: Build(zip), handler: () => new ThrowingHandler(new InvalidOperationException("never reached")));

        var status = await service.InstallAsync(new CancellationToken(canceled: true));

        Assert.Equal(FFmpegInstallState.Failed, status.Install.State);
        Assert.Equal("The FFmpeg download was cancelled.", status.Install.Error);
    }

    private FFmpegService Service(
        byte[]? payload = null, FFmpegBuild? build = null, string architecture = "arm64", Func<HttpMessageHandler>? handler = null)
    {
        var http = Substitute.For<IHttpClientFactory>();
        http.CreateClient(FFmpegService.HttpClientName).Returns(_ => new HttpClient(handler?.Invoke() ?? new StubHandler(payload ?? [])));

        var directories = Substitute.For<IHostDirectories>();
        directories.BinDirectory.Returns(Bin);

        var environment = new FFmpegEnvironment
        {
            Platform = "macos",
            Architecture = architecture,
            IsWindows = false,
            PathVariable = () => OnPath,
            RunVersionAsync = FakeRunAsync,
            Manifest = new FFmpegBuildManifest
            {
                SchemaVersion = FFmpegBuildManifest.SupportedSchemaVersion,
                Builds = build is null ? [] : [build],
            },
        };

        return new FFmpegService(
            NullLogger<FFmpegService>.Instance,
            new ConfigurationBuilder().AddInMemoryCollection(_configuration).Build(),
            directories, http, _broker, environment);
    }

    private static async Task<FFmpegVersionRun> FakeRunAsync(string path, CancellationToken cancellationToken)
    {
        var text = await File.ReadAllTextAsync(path, cancellationToken);

        return text.StartsWith("runs:", StringComparison.Ordinal)
            ? new FFmpegVersionRun(text["runs:".Length..], null)
            : new FFmpegVersionRun(null, "refused");
    }

    private static FFmpegBuild Build(
        byte[] zip, string? sha256 = null, long? size = null, string url = Url, Dictionary<string, string>? files = null) => new()
    {
        Rid = "macos-arm64",
        Version = "9.0",
        Publisher = "test",
        Licence = "GPL-2.0-or-later",
        Downloads =
        [
            new FFmpegDownload
            {
                Url = url,
                Sha256 = sha256 ?? Convert.ToHexString(SHA256.HashData(zip)).ToLowerInvariant(),
                Size = size ?? zip.Length,
                Files = files ?? new Dictionary<string, string> { ["ffmpeg"] = "pkg/bin/ffmpeg", ["ffprobe"] = "pkg/bin/ffprobe" },
            },
        ],
    };

    private static byte[] Zip(params (string Name, string Content)[] entries)
    {
        using var stream = new MemoryStream();

        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false));
                writer.Write(content);
            }
        }

        return stream.ToArray();
    }

    private static void Place(string folder, string name, string content)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, name), content);
    }

    private sealed class StubHandler(byte[] payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) });
    }

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromException<HttpResponseMessage>(exception);
        }
    }

    /// <summary>Answers, then loses the connection part way through the body.</summary>
    private sealed class DroppingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new DroppingStream()) });
    }

    private sealed class DroppingStream : MemoryStream
    {
        private bool _sent;

        public DroppingStream() : base([1, 2, 3, 4]) { }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_sent) throw new HttpIOException(HttpRequestError.ResponseEnded, "The response ended prematurely.");
            _sent = true;
            return base.ReadAsync(buffer, cancellationToken);
        }
    }
}
