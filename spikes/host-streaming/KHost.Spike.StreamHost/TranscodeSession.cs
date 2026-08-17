using System.Diagnostics;
using System.Globalization;

namespace KHost.Spike.StreamHost;

/// <summary>
/// One ffmpeg run publishing a song as HLS into a temp directory. The host owns this; every
/// consumer (screen, browser, Chromecast) is just an HTTP client of the resulting playlist.
/// </summary>
internal sealed class TranscodeSession : IDisposable
{
    public string Id { get; }
    public string Directory { get; }
    public string SourcePath { get; }
    public TimeSpan Offset { get; }
    public int PitchSemitones { get; }
    public DateTime StartedAt { get; }

    public string PlaylistName => "stream.m3u8";
    public bool IsComplete => _process?.HasExited ?? false;

    /// <summary>Wall-clock seconds until the playlist first had a segment a player could fetch.</summary>
    public double? FirstSegmentSeconds { get; private set; }

    private readonly Process? _process;
    private readonly string _log;

    public TranscodeSession(string sourcePath, TimeSpan offset, int pitchSemitones, string rootDirectory)
    {
        Id = Guid.NewGuid().ToString("n")[..8];
        SourcePath = sourcePath;
        Offset = offset;
        PitchSemitones = pitchSemitones;
        StartedAt = DateTime.UtcNow;

        Directory = Path.Combine(rootDirectory, Id);
        System.IO.Directory.CreateDirectory(Directory);
        _log = Path.Combine(Directory, "ffmpeg.log");

        var arguments = BuildArguments(sourcePath, offset, pitchSemitones, PlaylistName);
        File.WriteAllText(_log, $"ffmpeg {arguments}\n\n");

        _process = Process.Start(new ProcessStartInfo("ffmpeg", arguments)
        {
            WorkingDirectory = Directory,
            UseShellExecute = false,
            RedirectStandardError = true,
            CreateNoWindow = true,
        });

        // Held open so a failing transcode leaves something to read; ffmpeg blocks if nobody drains it.
        _ = Task.Run(async () =>
        {
            if (_process is null) return;
            var text = await _process.StandardError.ReadToEndAsync();
            await File.AppendAllTextAsync(_log, text);
        });

        _ = Task.Run(WatchForFirstSegmentAsync);
    }

    /// <summary>
    /// EVENT rather than VOD: transcoding runs far faster than realtime, but a player should be
    /// able to start on the first segment instead of waiting for the whole song.
    /// </summary>
    internal static string BuildArguments(string sourcePath, TimeSpan offset, int pitchSemitones, string playlistName)
    {
        var arguments = "-hide_banner -loglevel error";

        if (offset > TimeSpan.Zero)
            arguments += string.Format(CultureInfo.InvariantCulture, " -ss {0:F3}", offset.TotalSeconds);

        arguments += $" -i \"{sourcePath}\"";

        // Main@4.1 and AAC-LC are the intersection of what every Chromecast generation decodes and
        // what WKWebView/WebView2 play. Fixed GOP so segment boundaries land on keyframes.
        arguments += " -c:v libx264 -preset veryfast -profile:v main -level 4.1 -pix_fmt yuv420p"
                   + " -g 60 -keyint_min 60 -sc_threshold 0";

        var pitch = BuildPitchFilter(pitchSemitones);
        if (pitch.Length > 0) arguments += $" -af \"{pitch}\"";

        arguments += " -c:a aac -ar 44100 -ac 2 -b:a 128k";

        // MPEG-TS segments, not fMP4: fMP4/CMAF needs a newer Cast receiver, TS plays everywhere.
        arguments += " -f hls -hls_time 2 -hls_playlist_type event -hls_flags independent_segments"
                   + " -hls_segment_filename seg_%05d.ts";

        return arguments + $" {playlistName}";
    }

    private static string BuildPitchFilter(int semitones)
    {
        if (semitones == 0) return string.Empty;

        double ratio = Math.Pow(2.0, semitones / 12.0);
        return string.Format(
            CultureInfo.InvariantCulture,
            "asetrate=44100*{0:F6},aresample=44100,atempo={1:F6}",
            ratio, 1.0 / ratio);
    }

    private async Task WatchForFirstSegmentAsync()
    {
        var playlist = Path.Combine(Directory, PlaylistName);

        for (var i = 0; i < 600; i++)
        {
            if (File.Exists(playlist) && System.IO.Directory.EnumerateFiles(Directory, "*.ts").Any())
            {
                FirstSegmentSeconds = (DateTime.UtcNow - StartedAt).TotalSeconds;
                return;
            }

            await Task.Delay(50);
        }
    }

    public int SegmentCount()
        => System.IO.Directory.Exists(Directory)
            ? System.IO.Directory.EnumerateFiles(Directory, "*.ts").Count()
            : 0;

    public void Dispose()
    {
        try { if (_process is { HasExited: false }) _process.Kill(entireProcessTree: true); }
        catch { /* already gone */ }

        _process?.Dispose();

        try { System.IO.Directory.Delete(Directory, recursive: true); }
        catch { /* a consumer may still hold a segment open */ }
    }
}
