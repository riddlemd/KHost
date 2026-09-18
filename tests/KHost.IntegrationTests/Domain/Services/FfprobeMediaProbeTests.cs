using System.Diagnostics;
using KHost.Domain.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.IntegrationTests.Domain.Services;

/// <summary>The host's own probe, against real files. It is the fallback for everything a plugin
/// does not claim, so what it reports is what the importer and the faders see for most of a library.
/// </summary>
public class FfprobeMediaProbeTests : IDisposable
{
    private readonly string _workingDirectory =
        Path.Combine(Path.GetTempPath(), $"khost-probe-tests-{Guid.NewGuid():n}");

    private readonly FfprobeMediaProbe _probe = new(NullLogger<FfprobeMediaProbe>.Instance);

    public FfprobeMediaProbeTests() => Directory.CreateDirectory(_workingDirectory);

    public void Dispose() => Directory.Delete(_workingDirectory, recursive: true);

    /// <summary>A file scanned straight off disk has to carry its own length, which is the whole
    /// point of probing rather than trusting whatever a search result said.</summary>
    [RequiresFfmpegFact]
    public async Task Probe_AVideo_ReportsItsDuration()
    {
        var probe = await _probe.ProbeAsync(await CreateSilentMp4Async(seconds: 3));

        Assert.NotNull(probe);
        Assert.NotNull(probe.Duration);
        Assert.InRange(probe.Duration.Value.TotalSeconds, 2.5, 3.5);
    }

    /// <summary>Null is "nobody could tell", which a caller reads differently from an empty result.
    /// </summary>
    [Fact]
    public async Task Probe_AMissingFile_IsNull()
        => Assert.Null(await _probe.ProbeAsync(Path.Combine(_workingDirectory, "nope.mp4")));

    /// <summary>A file ffmpeg cannot open is the case a plugin's own container hits, and it must
    /// read as "could not tell" rather than as a file with nothing in it.</summary>
    [RequiresFfmpegFact]
    public async Task Probe_SomethingThatIsNotMedia_IsNull()
    {
        var path = Path.Combine(_workingDirectory, "not-media.mp4");
        await File.WriteAllTextAsync(path, "this is not a video");

        Assert.Null(await _probe.ProbeAsync(path));
    }

    /// <summary>One stream is nothing to separate, so it comes back with no tracks rather than one.
    /// </summary>
    [RequiresFfmpegFact]
    public async Task Probe_AVideoWithOneUnnamedAudioStream_ReportsNoTracks()
    {
        var probe = await _probe.ProbeAsync(await CreateSilentMp4Async(seconds: 1));

        Assert.NotNull(probe);
        Assert.Empty(probe.AudioTracks);
    }

    private async Task<string> CreateSilentMp4Async(int seconds)
    {
        var path = Path.Combine(_workingDirectory, $"silence-{Guid.NewGuid():n}.mp4");

        await RunFfmpegAsync(
            $"-f lavfi -i color=c=black:s=64x64:d={seconds} -f lavfi -i anullsrc=r=44100:cl=mono " +
            $"-t {seconds} -c:v libx264 -c:a aac -shortest \"{path}\"");

        return path;
    }

    private static async Task RunFfmpegAsync(string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo("ffmpeg", $"-hide_banner -loglevel error -y {arguments}")
        {
            UseShellExecute = false,
            RedirectStandardError = true,
        })!;

        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        Assert.True(process.ExitCode == 0, $"ffmpeg failed building the sample:\n{error}");
    }
}
