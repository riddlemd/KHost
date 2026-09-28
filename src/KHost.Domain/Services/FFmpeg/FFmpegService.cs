using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using FFMpegCore;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Common.Plugins;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace KHost.Domain.Services.FFmpeg;

public sealed class FFmpegService : BaseService, IFFmpegService, IDisposable
{
    public const string HttpClientName = "FFmpegDownload";

    /// <summary>The App Settings key naming a folder to look in first.</summary>
    public const string ConfigurationKey = "FFmpegPath";

    /// <summary>Download and unpack scratch, inside bin so the final move never crosses a volume.</summary>
    internal const string WorkFolderName = ".ffmpeg-install";

    /// <summary>Beside the programs, naming the pinned build they came from.</summary>
    internal const string MarkerFileName = "ffmpeg-build.json";

    // The Windows build unpacks to about 320 MB; this bounds an archive built to fill the disk.
    private const long MaxExpandedBytes = 1024L * 1024 * 1024;

    private static readonly TimeSpan VersionTimeout = TimeSpan.FromSeconds(20);

    private static readonly FFmpegTool[] Tools = [FFmpegTool.FFmpeg, FFmpegTool.FFprobe];

    private readonly IConfiguration _configuration;
    private readonly IHostDirectories _directories;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMessageBroker _broker;
    private readonly FFmpegEnvironment _environment;
    private readonly SemaphoreSlim _installLock = new(1, 1);
    private readonly Lock _statusLock = new();
    private readonly CancellationTokenSource _shutdown = new();

    private FFmpegStatus _status = new()
    {
        FFmpeg = new FFmpegToolStatus { Tool = FFmpegTool.FFmpeg },
        FFprobe = new FFmpegToolStatus { Tool = FFmpegTool.FFprobe },
    };

    public FFmpegService(
        ILogger<FFmpegService> logger,
        IConfiguration configuration,
        IHostDirectories directories,
        IHttpClientFactory httpClientFactory,
        IMessageBroker broker)
        : this(logger, configuration, directories, httpClientFactory, broker, FFmpegEnvironment.ThisMachine())
    {
    }

    internal FFmpegService(
        ILogger<FFmpegService> logger,
        IConfiguration configuration,
        IHostDirectories directories,
        IHttpClientFactory httpClientFactory,
        IMessageBroker broker,
        FFmpegEnvironment environment)
        : base(logger)
    {
        _configuration = configuration;
        _directories = directories;
        _httpClientFactory = httpClientFactory;
        _broker = broker;
        _environment = environment;
    }

    public FFmpegStatus Status
    {
        get { lock (_statusLock) return _status; }
    }

    public bool CanInstall => Build is not null;

    private FFmpegBuild? Build => _environment.Manifest.SelectFor(_environment.Platform, _environment.Architecture);

    private string? ConfiguredFolder => _configuration[ConfigurationKey];

    public string? Locate(FFmpegTool tool) => FFmpegLocator.Resolve(
        tool, ConfiguredFolder, _directories.BinDirectory, _environment.PathVariable(), _environment.IsWindows);

    public async Task<FFmpegStatus> CheckAsync(CancellationToken cancellationToken = default)
    {
        var ffmpegPath = Locate(FFmpegTool.FFmpeg);
        var ffprobePath = Locate(FFmpegTool.FFprobe);

        // Before the first await, so the next probe uses this ffprobe even while the versions are
        // still being asked for. The ffmpeg the host encodes with is resolved per song instead.
        PointFFMpegCoreAt(ffprobePath);

        var checks = await Task.WhenAll(
            DescribeAsync(FFmpegTool.FFmpeg, ffmpegPath, cancellationToken),
            DescribeAsync(FFmpegTool.FFprobe, ffprobePath, cancellationToken));

        foreach (var tool in checks)
            LogCheck(tool);

        return Update(status => status with { FFmpeg = checks[0], FFprobe = checks[1], HasChecked = true });
    }

    public async Task<FFmpegStatus> InstallAsync(CancellationToken cancellationToken = default)
    {
        if (!await _installLock.WaitAsync(0, CancellationToken.None))
            return Status;

        var work = Path.Combine(_directories.BinDirectory, WorkFolderName);

        try
        {
            // Not the caller's token alone: a page that starts an install and is navigated away
            // from must not take the half-done download with it.
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);

            var build = Build ?? throw new InvalidOperationException(
                $"KHost has no FFmpeg download for this computer ({_environment.Platform}-{_environment.Architecture}). "
                + "Install FFmpeg yourself and set its folder under Media in App Settings.");

            SetInstall(FFmpegInstallState.Downloading, 0);

            TryDeleteDirectory(work);
            var staged = Path.Combine(work, "staged");
            Directory.CreateDirectory(staged);

            await DownloadAndUnpackAsync(build, work, staged, linked.Token);

            SetInstall(FFmpegInstallState.Installing, null);

            await EnsureStagedProgramsRunAsync(staged, linked.Token);

            PutInPlace(staged, build);

            Logger.LogInformation("Installed FFmpeg {Version} from {Publisher} into {Directory}",
                build.Version, build.Publisher, _directories.BinDirectory);

            SetInstall(FFmpegInstallState.Succeeded, null);
        }
        catch (OperationCanceledException)
        {
            SetInstall(FFmpegInstallState.Failed, null, "The FFmpeg download was cancelled.");
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Installing FFmpeg failed");
            SetInstall(FFmpegInstallState.Failed, null, ex.Message);
        }
        finally
        {
            TryDeleteDirectory(work);
            _installLock.Release();
        }

        return Status.Install.State == FFmpegInstallState.Succeeded
            ? await CheckAsync(CancellationToken.None)
            : Status;
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        _shutdown.Dispose();
        _installLock.Dispose();
    }

    private async Task DownloadAndUnpackAsync(FFmpegBuild build, string work, string staged, CancellationToken cancellationToken)
    {
        var total = Math.Max(1, build.Downloads.Sum(d => d.Size));
        long finished = 0;

        for (var index = 0; index < build.Downloads.Count; index++)
        {
            var download = build.Downloads[index];
            var archive = Path.Combine(work, $"download-{index}.zip");
            var before = finished;

            var hash = await DownloadAsync(download, archive,
                bytes => SetInstall(FFmpegInstallState.Downloading, (double)(before + bytes) / total), cancellationToken);

            finished += download.Size;

            // Compared before the archive is opened: nothing from an unpinned file is ever read,
            // let alone written out and run.
            if (!hash.Equals(download.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"The download from {new Uri(download.Url).Host} does not match the checksum KHost pinned for it, so it was not opened.");

            SetInstall(FFmpegInstallState.Verifying, null);

            ExtractPrograms(archive, download, staged, _environment.IsWindows);

            File.Delete(archive);
        }

        foreach (var tool in Tools)
        {
            if (!File.Exists(Path.Combine(staged, FFmpegLocator.ExecutableName(tool, _environment.IsWindows))))
                throw new InvalidOperationException($"The pinned FFmpeg build has no {FFmpegLocator.ToolName(tool)} in it.");
        }
    }

    /// <summary>Takes only the named programs out of a pinned archive, once every entry is known to
    /// stay inside its folder.</summary>
    internal static void ExtractPrograms(string archivePath, FFmpegDownload download, string staged, bool windows)
    {
        using var archive = ZipFile.OpenRead(archivePath);

        ZipEntryGuard.EnsureContained(archive, staged, MaxExpandedBytes);

        foreach (var (name, entryPath) in download.Files)
        {
            var tool = name.Equals("ffmpeg", StringComparison.OrdinalIgnoreCase) ? FFmpegTool.FFmpeg
                : name.Equals("ffprobe", StringComparison.OrdinalIgnoreCase) ? FFmpegTool.FFprobe
                : throw new InvalidOperationException($"The FFmpeg build manifest names an unknown program '{name}'.");

            var entry = archive.GetEntry(entryPath)
                ?? throw new InvalidOperationException($"The download has no '{entryPath}' in it.");

            // Named by the host, never by the archive: what lands in bin is exactly the two programs.
            var target = Path.Combine(staged, FFmpegLocator.ExecutableName(tool, windows));
            entry.ExtractToFile(target, overwrite: true);

            if (!windows)
            {
                File.SetUnixFileMode(target,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }
        }
    }

    private async Task<string> DownloadAsync(
        FFmpegDownload download, string destination, Action<long> reportBytes, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(download.Url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException($"The FFmpeg build manifest lists '{download.Url}', which is not an https address.");

        using var client = _httpClientFactory.CreateClient(HttpClientName);

        HttpResponseMessage response;

        try
        {
            response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                $"Could not download FFmpeg from {uri.Host}: {ex.Message} Check this computer is online, then try again.", ex);
        }

        using var _ = response;
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var file = File.Create(destination);

        var buffer = new byte[81920];
        long written = 0;
        int read;

        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            written += read;

            // The pinned size is exact, so anything past it cannot match the checksum either;
            // stopping here keeps a hostile server from filling the disk first.
            if (written > download.Size)
                throw new InvalidOperationException(
                    $"The download from {uri.Host} is larger than the file KHost pinned, so it was stopped.");

            hasher.AppendData(buffer, 0, read);
            await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            reportBytes(written);
        }

        return Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
    }

    /// <summary>Runs each unpacked program before it replaces anything, so a copy this machine
    /// refuses never displaces one that worked.</summary>
    private async Task EnsureStagedProgramsRunAsync(string staged, CancellationToken cancellationToken)
    {
        foreach (var tool in Tools)
        {
            var path = Path.Combine(staged, FFmpegLocator.ExecutableName(tool, _environment.IsWindows));
            var run = await _environment.RunVersionAsync(path, cancellationToken);

            if (run.Version is null)
                throw new InvalidOperationException(
                    $"The downloaded {FFmpegLocator.ToolName(tool)} does not run on this computer: {run.Error}");
        }
    }

    private void PutInPlace(string staged, FFmpegBuild build)
    {
        Directory.CreateDirectory(_directories.BinDirectory);

        foreach (var tool in Tools)
        {
            var name = FFmpegLocator.ExecutableName(tool, _environment.IsWindows);
            var target = Path.Combine(_directories.BinDirectory, name);

            try
            {
                File.Move(Path.Combine(staged, name), target, overwrite: true);
            }
            catch (IOException ex)
            {
                throw new InvalidOperationException(
                    $"Could not replace {target}: {ex.Message} Stop any song that is playing, then try again.", ex);
            }
        }

        File.WriteAllText(Path.Combine(_directories.BinDirectory, MarkerFileName), JsonSerializer.Serialize(new
        {
            build.Rid,
            build.Version,
            build.Publisher,
            build.Licence,
            InstalledUtc = DateTime.UtcNow,
        }, JsonSerializerOptions.Web));
    }

    private async Task<FFmpegToolStatus> DescribeAsync(FFmpegTool tool, string? path, CancellationToken cancellationToken)
    {
        if (path is null)
            return new FFmpegToolStatus { Tool = tool };

        var run = await _environment.RunVersionAsync(path, cancellationToken);

        return new FFmpegToolStatus { Tool = tool, Path = path, Version = run.Version, Error = run.Error };
    }

    private void LogCheck(FFmpegToolStatus tool)
    {
        var name = FFmpegLocator.ToolName(tool.Tool);

        if (tool.IsUsable)
            Logger.LogInformation("{Tool} {Version} at {Path}", name, tool.Version, tool.Path);
        else if (tool.Path is not null)
            Logger.LogWarning("{Tool} at {Path} does not run: {Error}", name, tool.Path, tool.Error);
        else
            Logger.LogWarning("{Tool} not found: looked in the FFmpeg directory setting ({Configured}), {Bin} and PATH",
                name, string.IsNullOrWhiteSpace(ConfiguredFolder) ? "blank" : ConfiguredFolder, _directories.BinDirectory);
    }

    /// <summary>FFMpegCore reads one global folder for ffprobe, which a plugin using it shares too.</summary>
    private static void PointFFMpegCoreAt(string? ffprobePath)
    {
        var folder = ffprobePath is null ? "" : Path.GetDirectoryName(ffprobePath) ?? "";

        GlobalFFOptions.Configure(options => options.BinaryFolder = folder);
    }

    private void SetInstall(FFmpegInstallState state, double? progress, string? error = null)
    {
        Update(status =>
        {
            // Rounded to a whole percent so a fast download announces a hundred times, not per chunk.
            var rounded = progress is { } p ? Math.Floor(Math.Clamp(p, 0, 1) * 100) / 100 : (double?)null;

            return status with { Install = new FFmpegInstallProgress { State = state, Progress = rounded, Error = error } };
        });
    }

    private FFmpegStatus Update(Func<FFmpegStatus, FFmpegStatus> change)
    {
        FFmpegStatus before, after;

        lock (_statusLock)
        {
            before = _status;
            after = _status = change(_status);
        }

        // Outside the lock: a handler reading Status must not wait on the writer that woke it.
        if (after != before)
            _broker.Announce(new FFmpegChanged());

        return after;
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (Exception)
        {
            // Scratch; a locked copy is cleared by the next attempt.
        }
    }

    internal static async Task<FFmpegVersionRun> RunVersionAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(path, "-version")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            }) ?? throw new Win32Exception("the process did not start");

            // Both pipes drained together: a program that fills stderr blocks before it exits.
            var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = process.StandardError.ReadToEndAsync(cancellationToken);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(VersionTimeout);

            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }

                return new FFmpegVersionRun(null, $"it did not answer within {VersionTimeout.TotalSeconds:0} seconds.");
            }

            if (process.ExitCode != 0)
                return new FFmpegVersionRun(null, DescribeExit(process.ExitCode, await error));

            return FFmpegLocator.ParseVersion(await output) is { } version
                ? new FFmpegVersionRun(version, null)
                : new FFmpegVersionRun(null, "it ran but did not report a version.");
        }
        catch (Win32Exception ex)
        {
            return new FFmpegVersionRun(null, $"it could not be started ({ex.Message}).");
        }
    }

    private static string DescribeExit(int exitCode, string error)
    {
        // 128 + SIGKILL: how macOS stops a binary whose signature it will not accept, with nothing
        // written to stderr to say so.
        if (OperatingSystem.IsMacOS() && exitCode == 137)
            return "macOS stopped it from running, which usually means it is not signed for this Mac.";

        var firstLine = error.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();

        return firstLine is null ? $"it exited with code {exitCode}." : $"it exited with code {exitCode}: {firstLine}";
    }
}

/// <summary>What one <c>-version</c> run said: a version, or why there is none.</summary>
internal sealed record FFmpegVersionRun(string? Version, string? Error);

/// <summary>What the service needs from the machine, as one seam a test can replace.</summary>
internal sealed record FFmpegEnvironment
{
    public required string Platform { get; init; }
    public required string Architecture { get; init; }
    public required bool IsWindows { get; init; }
    public required Func<string?> PathVariable { get; init; }
    public required Func<string, CancellationToken, Task<FFmpegVersionRun>> RunVersionAsync { get; init; }
    public required FFmpegBuildManifest Manifest { get; init; }

    public static FFmpegEnvironment ThisMachine() => new()
    {
        Platform = PluginRid.Current,
        Architecture = PluginRid.CurrentArchitecture,
        IsWindows = OperatingSystem.IsWindows(),
        PathVariable = () => Environment.GetEnvironmentVariable("PATH"),
        RunVersionAsync = FFmpegService.RunVersionAsync,
        Manifest = FFmpegBuildManifest.Embedded,
    };
}
