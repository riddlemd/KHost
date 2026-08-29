using System.Diagnostics;
using System.Globalization;
using FFMpegCore;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KHost.Domain.Services;

/// <summary>One ffmpeg run feeds any number of consumers, all of them plain HTTP clients.</summary>
public sealed class HlsMediaStreamService : BaseService, IMediaStreamService, IDisposable
{
    public sealed class ServiceOptions
    {
        public const string SectionName = "MediaStream";

        /// <summary>Overwritten at startup with the live listening address, so a dynamic port works.</summary>
        public string BaseAddress { get; set; } = "http://localhost:5000";

        /// <summary>Scratch: under temp, not cache/, which holds real state.</summary>
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
        int pitch = 0,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Media file not found: {filePath}", filePath);

        var id = Guid.NewGuid().ToString("n");
        var directory = Path.Combine(_root, id);
        Directory.CreateDirectory(directory);

        var companionAudio = ResolveCompanionAudio(filePath);
        if (companionAudio is null && IsGraphicsOnly(filePath))
            Logger.LogWarning("No companion audio beside '{FilePath}'; the stream will be silent", filePath);

        var arguments = BuildArguments(
            filePath, startOffset, pitch, _options.SegmentSeconds, companionAudio);

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

        // A URL handed out early 404s, which a media element reports as "source not supported"
        // and never retries.
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
            Pitch = pitch,
        };
    }

    /// <summary>The file appears before a segment is listed in it, so existing is not playable.</summary>
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

    public string BuildImageUrl(Guid mediaId)
        => $"{_options.BaseAddress.TrimEnd('/')}/media/image/{mediaId}";

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
    /// EVENT so a consumer can start on the first segment. H.264 Main@4.1 with AAC-LC is the
    /// intersection of what browsers, WKWebView and every Chromecast generation decode.
    /// </summary>
    internal static string BuildArguments(
        string filePath,
        TimeSpan startOffset,
        int pitch,
        int segmentSeconds,
        string? companionAudioPath = null)
    {
        var arguments = "-hide_banner -loglevel error";

        // CDG decoding is stateful, so an input seek lands mid-packet and the graphics decode to
        // garbage. A paired source seeks on the output instead and eats the frames.
        var seekOnOutput = companionAudioPath is not null;

        if (startOffset > TimeSpan.Zero && !seekOnOutput)
            arguments += string.Format(CultureInfo.InvariantCulture, " -ss {0:F3}", startOffset.TotalSeconds);

        arguments += $" -i \"{filePath}\"";

        if (companionAudioPath is not null)
        {
            // Without the mapping ffmpeg picks one stream per type from the first input that has
            // one, and a .cdg carries no audio at all.
            arguments += $" -i \"{companionAudioPath}\" -map 0:v:0 -map 1:a:0";
        }

        if (startOffset > TimeSpan.Zero && seekOnOutput)
            arguments += string.Format(CultureInfo.InvariantCulture, " -ss {0:F3}", startOffset.TotalSeconds);

        var segment = Math.Max(1, segmentSeconds);

        // Keyframes on time, not a frame count: -g is in frames, so it matches the segment length
        // at exactly one source frame rate, and the muxer can only cut where a keyframe already is.
        arguments += " -c:v libx264 -preset veryfast -profile:v main -level 4.1 -pix_fmt yuv420p"
                   + string.Format(
                        CultureInfo.InvariantCulture,
                        " -force_key_frames \"expr:gte(t,n_forced*{0})\" -sc_threshold 0",
                        segment);

        var pitchFilter = BuildPitchFilter(pitch);
        if (pitchFilter.Length > 0) arguments += $" -af \"{pitchFilter}\"";

        arguments += " -c:a aac -ar 44100 -ac 2 -b:a 128k";

        // MPEG-TS segments rather than fMP4: CMAF needs a newer Cast receiver, TS plays everywhere.
        arguments += string.Format(
            CultureInfo.InvariantCulture,
            " -f hls -hls_time {0} -hls_playlist_type event -hls_flags independent_segments"
            + " -hls_segment_filename seg_%05d.ts",
            segment);

        return arguments + $" {PlaylistFileName}";
    }

    internal static bool IsGraphicsOnly(string filePath)
        => Path.GetExtension(filePath).Equals(".cdg", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A .cdg holds only graphics; its audio is the same-named .mp3 beside it. Only .mp3 — CD+G
    /// rips have always shipped that way, so a same-named file in any other format is not the pair.
    /// </summary>
    internal static string? ResolveCompanionAudio(string filePath)
    {
        if (!IsGraphicsOnly(filePath)) return null;

        var companion = Path.ChangeExtension(filePath, ".mp3");
        return File.Exists(companion) ? companion : null;
    }

    private static string BuildPitchFilter(int semitones)
    {
        if (semitones == 0) return string.Empty;

        double ratio = Math.Pow(2.0, semitones / 12.0);

        // asetrate reinterprets whatever rate reaches it, so the leading resample is what makes
        // its base true: a 48kHz source would otherwise carry an uncorrected 44100/48000 as well.
        return string.Format(
            CultureInfo.InvariantCulture,
            "aresample=44100,asetrate=44100*{0:F6},aresample=44100,atempo={1:F6}",
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
