using System.Diagnostics;
using System.IO.Compression;
using KHost.Domain.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.IntegrationTests.Domain.Services;

/// <summary>A zipped pair's length is its audio's, read by real ffprobe.</summary>
public class CdgMediaProbeTests : IDisposable
{
    private readonly string _workingDirectory = Directory.CreateTempSubdirectory("khost-cdgprobe-it").FullName;

    public void Dispose() => Directory.Delete(_workingDirectory, recursive: true);

    [RequiresFfmpegFact]
    public async Task ProbeAsync_AZippedPair_TakesItsDurationFromTheAudioInside()
    {
        var audio = Path.Combine(_workingDirectory, "song.mp3");
        using (var process = Process.Start(new ProcessStartInfo("ffmpeg",
                   $"-hide_banner -loglevel error -y -f lavfi -i sine=frequency=440 -t 3 -c:a libmp3lame \"{audio}\"")
               { UseShellExecute = false, RedirectStandardError = true })!)
        {
            await process.WaitForExitAsync();
        }

        var zip = Path.Combine(_workingDirectory, "song.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            archive.CreateEntryFromFile(audio, "song.mp3");
            using var graphics = archive.CreateEntry("song.cdg").Open();
            graphics.Write(new byte[24 * 300 * 3]);
        }

        File.Delete(audio);

        var probe = new CdgMediaProbe(new FfprobeMediaProbe(NullLogger<FfprobeMediaProbe>.Instance));
        var result = await probe.ProbeAsync(zip);

        Assert.NotNull(result?.Duration);
        Assert.InRange(result.Duration.Value.TotalSeconds, 2.5, 3.5);
    }
}
