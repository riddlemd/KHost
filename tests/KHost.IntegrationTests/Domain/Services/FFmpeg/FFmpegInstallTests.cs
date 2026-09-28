using System.Diagnostics;
using FFMpegCore;
using KHost.Abstractions.Models;
using KHost.Common.Plugins;
using KHost.Domain.Services;
using KHost.Domain.Services.FFmpeg;
using KHost.Domain.Services.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.IntegrationTests.Domain.Services.FFmpeg;

/// <summary>Installs this machine's pinned build for real, from the publisher, into a temp folder.</summary>
/// <remarks>Serial: installing points FFMpegCore's one global folder at that temp folder, which
/// every other probe in the run reads.</remarks>
[Collection(nameof(FFmpegInstallCollection))]
public sealed class FFmpegInstallTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("khost-ffmpeg-install-");
    private readonly string _savedBinaryFolder = GlobalFFOptions.Current.BinaryFolder;

    private string Bin => Path.Combine(_root.FullName, "bin");

    public void Dispose()
    {
        GlobalFFOptions.Configure(options => options.BinaryFolder = _savedBinaryFolder);

        try { _root.Delete(recursive: true); } catch (IOException) { }
    }

    [RequiresPinnedFFmpegBuildFact]
    public async Task InstallAsync_ThisMachinesPinnedBuild_InstallsProgramsThatRun()
    {
        var service = new FFmpegService(
            NullLogger<FFmpegService>.Instance,
            new ConfigurationBuilder().Build(),
            new HostDirectories(Bin),
            new PlainHttpClientFactory(),
            new MessageBroker(NullLogger<MessageBroker>.Instance));

        var status = await service.InstallAsync();

        Assert.True(status.Install.State == FFmpegInstallState.Succeeded, status.Install.Error);
        Assert.True(status.IsReady);
        Assert.StartsWith(Bin, status.FFmpeg.Path);
        Assert.StartsWith(Bin, status.FFprobe.Path);

        foreach (var path in new[] { status.FFmpeg.Path!, status.FFprobe.Path! })
        {
            var (exitCode, output) = await RunVersionAsync(path);

            Assert.True(exitCode == 0, $"{path} -version exited {exitCode}: {output}");
            Assert.Matches(@"^ff(mpeg|probe) version ", output);
        }
    }

    private static async Task<(int ExitCode, string Output)> RunVersionAsync(string path)
    {
        using var process = Process.Start(new ProcessStartInfo(path, "-version")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;

        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return (process.ExitCode, await output + await error);
    }

    private sealed class PlainHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new() { Timeout = Timeout.InfiniteTimeSpan };
    }
}

[CollectionDefinition(nameof(FFmpegInstallCollection), DisableParallelization = true)]
public sealed class FFmpegInstallCollection;

/// <summary>xUnit 2 cannot skip at runtime, so the decision is made in the constructor.</summary>
/// <remarks>Skipped with the rest of the environment tests, which is how an offline run opts out.</remarks>
public sealed class RequiresPinnedFFmpegBuildFactAttribute : FactAttribute
{
    public RequiresPinnedFFmpegBuildFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("KHOST_SKIP_ENVIRONMENT_TESTS") is { Length: > 0 })
            Skip = "KHOST_SKIP_ENVIRONMENT_TESTS is set; this test downloads FFmpeg";
        else if (FFmpegBuildManifest.Embedded.SelectFor(PluginRid.Current, PluginRid.CurrentArchitecture) is null)
            Skip = "no FFmpeg build is pinned for this platform";
    }
}
