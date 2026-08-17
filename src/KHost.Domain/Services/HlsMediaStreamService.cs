using System.Diagnostics;
using System.Globalization;
using FFMpegCore;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KHost.Domain.Services;

/// <summary>
/// Transcodes to HLS here on the host, so every screen is only an HTTP consumer of the result.
/// One ffmpeg run feeds any number of consumers; the previous design ran one per screen.
/// </summary>
public sealed class HlsMediaStreamService : BaseService, IMediaStreamService, IDisposable
{
    public sealed class ServiceOptions
    {
        public const string SectionName = "MediaStream";

        /// <summary>
        /// Base URL consumers fetch from. Program.cs overwrites this with the host's live
        /// listening address at startup, so a dynamic port still produces reachable URLs.
        /// </summary>
        public string BaseAddress { get; set; } = "http://localhost:5000";

        /// <summary>
        /// Scratch space. Defaults under the temp directory rather than <c>cache/</c>, which holds
        /// real state — segments are rebuilt per session and deleted with it.
        /// </summary>
        public string? WorkingDirectory { get; set; }

        /// <summary>Shorter segments start sooner; longer ones survive a worse network.</summary>
        public int SegmentSeconds { get; set; } = 2;
    }

    internal const string PlaylistFileName = "stream.m3u8";

    /// <summary>Generous: a first segment normally lands in well under a second.</summary>
    private static readonly TimeSpan PlaylistTimeout = TimeSpan.FromSeconds(15);

    private readonly ServiceOptions _options;
    private readonly Dictionary<string, Session> _sessions = [];
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly string _root;

    public HlsMediaStreamService(ILogger<HlsMediaStreamService> logger, IOptions<ServiceOptions> options)
        : base(logger)
    {
        _options = options.Value;
        _root = string.IsNullOrWhiteSpace(_options.WorkingDirectory)
            ? Path.Combine(Path.GetTempPath(), "khost-streams")
            : _options.WorkingDirectory;

        Directory.CreateDirectory(_root);
    }

    public async Task<MediaStreamSession> OpenAsync(
        string filePath,
        TimeSpan startOffset = default,
        int pitchSemitones = 0,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Media file not found: {filePath}", filePath);

        var id = Guid.NewGuid().ToString("n");
        var directory = Path.Combine(_root, id);
        Directory.CreateDirectory(directory);

        var arguments = BuildArguments(filePath, startOffset, pitchSemitones, _options.SegmentSeconds);

        Logger.LogInformation("Opening stream {SessionId} for '{FilePath}' at {Offset}", id, filePath, startOffset);
        Logger.LogDebug("ffmpeg {Arguments}", arguments);

        var process = Process.Start(new ProcessStartInfo(ResolveFfmpegPath(), arguments)
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
            RedirectStandardError = true,
            CreateNoWindow = true,
        }) ?? throw new InvalidOperationException("Failed to start ffmpeg");

        var session = new Session(id, directory, process);

        // ffmpeg blocks once the stderr pipe fills, so it has to be drained even when discarded.
        _ = Task.Run(async () =>
        {
            var text = await process.StandardError.ReadToEndAsync(CancellationToken.None);
            if (!string.IsNullOrWhiteSpace(text))
                Logger.LogWarning("ffmpeg for {SessionId}: {Error}", id, text.Trim());
        }, CancellationToken.None);

        await _lock.WaitAsync(cancellationToken);
        try { _sessions[id] = session; }
        finally { _lock.Release(); }

        // ffmpeg needs a moment to write the playlist. Handing out the URL before then gives the
        // screen a 404, which a media element reports as "source not supported" and never retries.
        if (!await WaitForPlaylistAsync(directory, cancellationToken))
        {
            await CloseAsync(id);
            throw new InvalidOperationException(
                $"ffmpeg produced no playlist for '{filePath}'. See {Path.Combine(directory, "ffmpeg.log")}.");
        }

        return new MediaStreamSession
        {
            Id = id,
            SourcePath = filePath,
            PlaylistUrl = $"{_options.BaseAddress.TrimEnd('/')}/media/{id}/{PlaylistFileName}",
            StartOffset = startOffset,
            PitchSemitones = pitchSemitones,
        };
    }

    /// <summary>
    /// Waits for a playlist that names at least one segment. The playlist file appears before the
    /// first segment is listed in it, so the file existing is not enough to be playable.
    /// </summary>
    private static async Task<bool> WaitForPlaylistAsync(string directory, CancellationToken cancellationToken)
    {
        var playlist = Path.Combine(directory, PlaylistFileName);
        var deadline = DateTime.UtcNow + PlaylistTimeout;

        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(playlist))
            {
                try
                {
                    if (File.ReadAllText(playlist).Contains(".ts", StringComparison.Ordinal)) return true;
                }
                catch (IOException)
                {
                    // ffmpeg is mid-rewrite; the next poll gets a whole file.
                }
            }

            await Task.Delay(25, cancellationToken);
        }

        return false;
    }

    public async Task CloseAsync(string sessionId)
    {
        Session? session;

        await _lock.WaitAsync();
        try
        {
            if (!_sessions.Remove(sessionId, out session)) return;
        }
        finally { _lock.Release(); }

        Logger.LogInformation("Closing stream {SessionId}", sessionId);
        session!.Dispose();
    }

    public async Task CloseAllAsync()
    {
        List<Session> snapshot;

        await _lock.WaitAsync();
        try
        {
            snapshot = [.. _sessions.Values];
            _sessions.Clear();
        }
        finally { _lock.Release(); }

        foreach (var session in snapshot) session.Dispose();
    }

    public string? ResolveArtifact(string sessionId, string fileName)
    {
        // Anything that is not a bare file name is rejected before it reaches the filesystem.
        if (string.IsNullOrWhiteSpace(fileName)
            || fileName.Contains('/') || fileName.Contains('\\')
            || fileName.Contains("..", StringComparison.Ordinal)
            || Path.IsPathRooted(fileName))
            return null;

        string directory;

        _lock.Wait();
        try
        {
            if (!_sessions.TryGetValue(sessionId, out var session)) return null;
            directory = session.Directory;
        }
        finally { _lock.Release(); }

        var path = Path.Combine(directory, fileName);
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// EVENT rather than VOD: transcoding outruns playback, but a consumer should be able to start
    /// on the first segment instead of waiting for the whole song. H.264 Main@4.1 with AAC-LC is
    /// the intersection of what browsers, WKWebView and every Chromecast generation will decode.
    /// </summary>
    internal static string BuildArguments(string filePath, TimeSpan startOffset, int pitchSemitones, int segmentSeconds)
    {
        var arguments = "-hide_banner -loglevel error";

        if (startOffset > TimeSpan.Zero)
            arguments += string.Format(CultureInfo.InvariantCulture, " -ss {0:F3}", startOffset.TotalSeconds);

        arguments += $" -i \"{filePath}\"";

        // Fixed GOP with no scene-cut detection, so every segment starts on a keyframe.
        arguments += " -c:v libx264 -preset veryfast -profile:v main -level 4.1 -pix_fmt yuv420p"
                   + " -g 60 -keyint_min 60 -sc_threshold 0";

        var pitch = BuildPitchFilter(pitchSemitones);
        if (pitch.Length > 0) arguments += $" -af \"{pitch}\"";

        arguments += " -c:a aac -ar 44100 -ac 2 -b:a 128k";

        // MPEG-TS segments rather than fMP4: CMAF needs a newer Cast receiver, TS plays everywhere.
        arguments += string.Format(
            CultureInfo.InvariantCulture,
            " -f hls -hls_time {0} -hls_playlist_type event -hls_flags independent_segments"
            + " -hls_segment_filename seg_%05d.ts",
            Math.Max(1, segmentSeconds));

        return arguments + $" {PlaylistFileName}";
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

    private static string ResolveFfmpegPath()
    {
        var exeName = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";

        var folder = GlobalFFOptions.Current.BinaryFolder;
        if (!string.IsNullOrEmpty(folder))
        {
            var candidate = Path.Combine(folder, exeName);
            if (File.Exists(candidate)) return candidate;
        }

        return exeName;
    }

    public void Dispose()
    {
        CloseAllAsync().GetAwaiter().GetResult();
        _lock.Dispose();
    }

    private sealed class Session(string id, string directory, Process process) : IDisposable
    {
        public string Id { get; } = id;
        public string Directory { get; } = directory;

        public void Dispose()
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch { /* already gone */ }

            process.Dispose();

            // A consumer may still hold a segment open; the directory is scratch either way.
            try { System.IO.Directory.Delete(Directory, recursive: true); }
            catch { /* swept on the next start */ }
        }
    }
}
