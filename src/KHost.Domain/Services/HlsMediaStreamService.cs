using System.Diagnostics;
using System.Globalization;
using FFMpegCore;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using KHost.Common.Media;
using KHost.Domain.Services.BurnIn;

namespace KHost.Domain.Services;

/// <summary>One ffmpeg run feeds any number of consumers, all of them plain HTTP clients.</summary>
public sealed class HlsMediaStreamService : BaseService, IMediaStreamService, IBurnInStreamService, IDisposable
{
    public sealed class ServiceOptions
    {
        public const string SectionName = "MediaStream";

        /// <summary>Overwritten at startup with the live address, so a dynamic port works.</summary>
        public string BaseAddress { get; set; } = "http://localhost:5000";

        /// <summary>Scratch: under temp, not cache/, which holds real state.</summary>
        public string? WorkingDirectory { get; set; }

        /// <summary>Shorter segments start sooner; longer ones survive a worse network.</summary>
        public int SegmentSeconds { get; set; } = 2;

    }

    internal const string PlaylistFileName = "stream.m3u8";

    /// <summary>Generous: a first segment normally lands in well under a second.</summary>
    private static readonly TimeSpan PlaylistTimeout = TimeSpan.FromSeconds(15);

    private readonly IOptionsMonitor<ServiceOptions> _options;
    private readonly Dictionary<string, Session> _sessions = [];
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly IPlayableMediaSourceService _playableSources;
    private readonly string _root;
    private int _disposed;

    public HlsMediaStreamService(
        ILogger<HlsMediaStreamService> logger,
        IOptionsMonitor<ServiceOptions> options,
        IPlayableMediaSourceService playableSources)
        : base(logger)
    {
        _options = options;
        _playableSources = playableSources;

        // The root is resolved once and the rest is read live. Moving the directory under running
        // sessions would strand the segments they are already serving, where a changed segment
        // length only decides how the next stream is cut.
        var working = string.IsNullOrWhiteSpace(Options.WorkingDirectory)
            ? Path.Combine(Path.GetTempPath(), "khost-streams")
            : Options.WorkingDirectory;

        _root = working;

        Directory.CreateDirectory(_root);
    }

    /// <summary>Read per use, never snapshotted: a host changing the segment length in App
    /// Settings expects the next song to honour it, not the next launch.</summary>
    private ServiceOptions Options => _options.CurrentValue;

    /// <summary>A fresh session's id and scratch directory, shared by both open paths.</summary>
    private (string Id, string Directory) NewSession()
    {
        var id = Guid.NewGuid().ToString("n");
        var directory = Path.Combine(_root, id);
        Directory.CreateDirectory(directory);

        return (id, directory);
    }

    private async Task RegisterAsync(Session session, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try { _sessions[session.Id] = session; }
        finally { _lock.Release(); }
    }

    public async Task<MediaStreamSession> OpenWithoutEncodeAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Media file not found: {filePath}", filePath);

        var (id, directory) = NewSession();
        var session = new Session(id, directory, process: null);

        await RegisterAsync(session, cancellationToken);

        Logger.LogInformation("Opened {SessionId} for '{FilePath}' with no encode", id, filePath);

        return new MediaStreamSession
        {
            Id = id,
            SourcePath = filePath,

            // Nothing to play as one stream: whatever wrote here names its own files, and they are
            // reached through the same /media/{sessionId}/{fileName} route the segments use.
            PlaylistUrl = null,
            WorkingDirectory = directory,
            StartOffset = TimeSpan.Zero,
            Pitch = 0,
            Tempo = 0,
        };
    }

    public Task<MediaStreamSession> OpenAsync(
        string filePath,
        TimeSpan startOffset = default,
        int pitch = 0,
        int tempo = 0,
        AudioMix? mix = null,
        CancellationToken cancellationToken = default)
        => OpenEncodeAsync(filePath, startOffset, pitch, tempo, mix, words: null, backgroundPath: null, cancellationToken);

    public Task<MediaStreamSession> OpenBurningInAsync(
        string filePath,
        TimeSpan startOffset,
        int pitch,
        int tempo,
        AudioMix? mix,
        TimedLyrics words,
        string? backgroundPath,
        CancellationToken cancellationToken = default)
        => OpenEncodeAsync(filePath, startOffset, pitch, tempo, mix, words, backgroundPath, cancellationToken);

    private async Task<MediaStreamSession> OpenEncodeAsync(
        string filePath,
        TimeSpan startOffset,
        int pitch,
        int tempo,
        AudioMix? mix,
        TimedLyrics? words,
        string? backgroundPath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Media file not found: {filePath}", filePath);

        var (id, directory) = NewSession();

        // Into the session's own directory, so a converted copy is swept with the segments when
        // the session closes and nothing has to remember it exists.
        var source = await _playableSources.ResolvePlayableAsync(filePath, directory, cancellationToken);

        // Everything below reads the resolved path: a companion .mp3 sits beside the original, but
        // what ffmpeg opens, and what decides the graphics-only frame rate, is what it will read.
        var companionAudio = ResolveCompanionAudio(source);
        if (companionAudio is null && IsGraphicsOnly(source))
            Logger.LogWarning("No companion audio beside '{FilePath}'; the stream will be silent", source);

        var burnIn = words is null
            ? null
            : await PlanBurnInAsync(source, words, backgroundPath, startOffset, tempo, cancellationToken);

        var arguments = BuildArguments(
            source, startOffset, pitch, tempo, Options.SegmentSeconds, companionAudio, mix, burnIn?.Overlay);

        Logger.LogInformation(
            "Opening stream {SessionId} for '{FilePath}' at {Offset}{BurnIn}",
            id, source, startOffset, burnIn is null ? "" : $", words burned in over {burnIn.Overlay.Base}");
        Logger.LogDebug("ffmpeg {Arguments}", arguments);

        var process = Process.Start(new ProcessStartInfo(ResolveFfmpegPath(), arguments)
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardInput = burnIn is not null,
            CreateNoWindow = true,
        }) ?? throw new InvalidOperationException("Failed to start ffmpeg");

        var session = new Session(id, directory, process);

        if (burnIn is not null) session.StartPainting(burnIn, process.StandardInput.BaseStream, Logger);

        // Registered with a token that never cancels: a caller giving up between Start and here
        // must still find the process in _sessions, or nothing ever tears it down and it runs
        // until the app exits rather than until the next CloseAsync/CloseAllAsync.
        await RegisterAsync(session, CancellationToken.None);

        // ffmpeg blocks once the stderr pipe fills, so it has to be drained even when discarded.
        _ = Task.Run(async () =>
        {
            var text = await process.StandardError.ReadToEndAsync(CancellationToken.None);
            if (!string.IsNullOrWhiteSpace(text))
                Logger.LogWarning("ffmpeg for {SessionId}: {Error}", id, text.Trim());
        }, CancellationToken.None);

        try
        {
            // A URL handed out early 404s, which a media element reports as "source not supported"
            // and never retries.
            if (!await WaitForPlaylistAsync(directory, cancellationToken))
            {
                await CloseAsync(id);
                throw new InvalidOperationException(
                    $"ffmpeg produced no playlist for '{filePath}'. See the warning logged for session {id}.");
            }
        }
        catch (OperationCanceledException)
        {
            // The session is already registered above, so a caller who gives up mid-wait still
            // gets the process killed and the directory swept instead of it outliving this call.
            await CloseAsync(id);
            throw;
        }

        return new MediaStreamSession
        {
            Id = id,
            SourcePath = filePath,
            PlaylistUrl = $"{Options.BaseAddress.TrimEnd('/')}/media/{id}/{PlaylistFileName}",
            StartOffset = startOffset,
            Pitch = pitch,
            Tempo = tempo,
        };
    }

    /// <summary>What the words go over and how many frames of them to paint.</summary>
    /// <remarks>The source's own picture when it has one; else the venue's chosen background; else a
    /// plain black frame, which is what the screen shows behind a song with no picture. A cover
    /// image stored as a video stream is not a picture to play under the words — it is one frame.
    ///
    /// <para>Painted from the playhead, except for a source that seeks on its output: ffmpeg then
    /// discards everything before the playhead, painted frames included.</para></remarks>
    private async Task<BurnInPlan> PlanBurnInAsync(
        string source, TimedLyrics words, string? backgroundPath, TimeSpan startOffset, int tempo,
        CancellationToken cancellationToken)
    {
        var (hasVideo, duration) = await ProbePictureAsync(source, cancellationToken);

        var basePicture = hasVideo
            ? BurnInBase.SourceVideo
            : backgroundPath is not null && File.Exists(backgroundPath) ? BurnInBase.Background : BurnInBase.Fill;

        var overlay = new BurnInOverlay(
            BurnInOverlay.DefaultWidth, BurnInOverlay.DefaultHeight, BurnInOverlay.DefaultFramesPerSecond,
            basePicture, basePicture == BurnInBase.Background ? backgroundPath : null);

        // Laid over a picture nobody made with the words in mind, so the band they sit in is darkened.
        var painter = new TimedLyricsPainter(words, overlay.Width, overlay.Height, scrim: basePicture != BurnInBase.Fill);

        var start = IsGraphicsOnly(source) ? 0 : startOffset.TotalSeconds;
        var rate = StreamRate.FromTempo(tempo);
        var end = Math.Max(painter.DurationSeconds, duration);

        return new BurnInPlan(
            overlay, painter, start, rate,
            BurnInFramePump.FramesFor(end, start, rate, overlay.FramesPerSecond),
            BurnInFramePump.WorkersFor(Environment.ProcessorCount));
    }

    /// <summary>Whether the source carries a moving picture, and how long it runs.</summary>
    /// <remarks>A probe that fails answers "no picture, no length": the words still go over black
    /// for as long as they last, which beats refusing the song.</remarks>
    private async Task<(bool HasVideo, double DurationSeconds)> ProbePictureAsync(
        string source, CancellationToken cancellationToken)
    {
        try
        {
            var analysis = await FFProbe.AnalyseAsync(source, cancellationToken: cancellationToken);
            var hasVideo = analysis.VideoStreams.Any(
                stream => stream.Disposition?.GetValueOrDefault("attached_pic") != true);

            return (hasVideo, analysis.Duration.TotalSeconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogWarning(ex, "Could not probe '{FilePath}' for a picture; burning the words in over black", source);
            return (false, 0);
        }
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
        => $"{Options.BaseAddress.TrimEnd('/')}/media/image/{mediaId}";

    public string BuildArtifactUrl(string sessionId, string fileName)
        => $"{Options.BaseAddress.TrimEnd('/')}/media/{sessionId}/{fileName}";

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

    /// <summary>EVENT playlist type so a consumer can start on the first segment.</summary>
    /// <remarks>H.264 Main@4.1 + AAC-LC decodes in every browser and in WKWebView, which is what
    /// LocalScreen renders through; a display provider's device is a third consumer it also suits.</remarks>
    internal static string BuildArguments(
        string filePath,
        TimeSpan startOffset,
        int pitch,
        int tempo,
        int segmentSeconds,
        string? companionAudioPath = null,
        AudioMix? mix = null,
        BurnInOverlay? burnIn = null)
    {
        var arguments = "-hide_banner -loglevel error";

        // CDG decoding is stateful, so an input seek lands mid-packet and the graphics decode to
        // garbage. Such a source seeks on the output instead and eats the frames.
        //
        // Asked of the graphics, not of the companion audio: a .cdg with no .mp3 beside it is still
        // a stateful decode, and keying this off the pairing seeked it on the input and drew
        // garbage for the one case that already had no sound. Reused below for the frame-rate
        // decision, which asks the same question of the same file.
        var isGraphicsOnly = IsGraphicsOnly(filePath);

        // A .cdg decodes a frame only when its graphics change, so its picture stops at the last
        // change, seconds before the audio. A player then stalls on the missing video rather than
        // ending, and the song never concludes. The picture is held until the audio ends it.
        // Never without audio: an endless picture with nothing to end it would never finish.
        var holdsPictureToTheAudio = isGraphicsOnly && companionAudioPath is not null;

        if (startOffset > TimeSpan.Zero && !isGraphicsOnly)
            arguments += string.Format(CultureInfo.InvariantCulture, " -ss {0:F3}", startOffset.TotalSeconds);

        arguments += $" -i \"{filePath}\"";

        if (companionAudioPath is not null)
        {
            arguments += $" -i \"{companionAudioPath}\"";

            // Without the mapping ffmpeg picks one stream per type from the first input that has
            // one, and a .cdg carries no audio at all. A burn-in maps its own, after its inputs.
            if (burnIn is null) arguments += " -map 0:v:0 -map 1:a:0";
        }

        // Inputs come before the output seek below, or that -ss would bind to the next input.
        var pipeInput = companionAudioPath is null ? 1 : 2;
        if (burnIn is not null) arguments += BurnInInputs(burnIn);

        if (startOffset > TimeSpan.Zero && isGraphicsOnly)
            arguments += string.Format(CultureInfo.InvariantCulture, " -ss {0:F3}", startOffset.TotalSeconds);

        var segment = Math.Max(1, segmentSeconds);

        // A .cdg only emits a frame when the graphics change, so x264 is handed a wildly variable
        // rate and encodes far more than the picture needs. Measured on two songs: 110 and 154
        // CPU-seconds without this against 33 and 44 with it, for the same segments either way.
        if (isGraphicsOnly)
            arguments += " -r 30";

        // Keyframes on time, not a frame count: -g is in frames, so it matches the segment length
        // at exactly one source frame rate, and the muxer can only cut where a keyframe already is.
        arguments += " -c:v libx264 -preset veryfast -profile:v main -level 4.1 -pix_fmt yuv420p"
                   + string.Format(
                        CultureInfo.InvariantCulture,
                        " -force_key_frames \"expr:gte(t,n_forced*{0})\" -sc_threshold 0",
                        segment);

        var audioFilter = BuildAudioFilter(pitch, tempo);
        var mixGraph = BuildMixGraph(mix, audioFilter);

        if (burnIn is not null)
        {
            arguments += BurnInMapping(
                burnIn, pipeInput, tempo, companionAudioPath is null ? 0 : 1, mixGraph, audioFilter, holdsPictureToTheAudio);
        }
        else if (mixGraph.Length > 0)
        {
            // Explicit maps: the graph names the audio output, and without saying so ffmpeg would
            // also carry one of the raw tracks through beside it. The picture is optional ("?")
            // because a multi-track container can be audio alone — a mix is the one case where a
            // missing video stream would otherwise be a fatal unmatched mapping rather than a
            // silently dropped one.
            arguments += $" -filter_complex \"{mixGraph}\" -map 0:v:0? -map \"[a]\"";
        }
        else if (audioFilter.Length > 0)
        {
            arguments += $" -af \"{audioFilter}\"";
        }

        // -vf rather than a filter_complex: it composes with the CDG mapping above, and ffmpeg
        // drops it silently on a source with no video rather than failing on an unmatched label.
        // A burn-in retimes its picture inside its own graph instead.
        var videoFilter = holdsPictureToTheAudio
            ? string.Join(',', new[] { HoldLastFrame, BuildVideoFilter(tempo) }.Where(f => f.Length > 0))
            : BuildVideoFilter(tempo);
        if (videoFilter.Length > 0 && burnIn is null) arguments += $" -vf \"{videoFilter}\"";

        if (holdsPictureToTheAudio) arguments += " -shortest";

        arguments += " -c:a aac -ar 44100 -ac 2 -b:a 128k";

        // MPEG-TS segments rather than fMP4: TS plays everywhere, and CMAF needs a newer device
        // than some display providers reach. Wanting CMAF means asking the provider first.
        arguments += string.Format(
            CultureInfo.InvariantCulture,
            " -f hls -hls_time {0} -hls_playlist_type event -hls_flags independent_segments"
            + " -hls_segment_filename seg_%05d.ts",
            segment);

        return arguments + $" {PlaylistFileName}";
    }

    internal static bool IsGraphicsOnly(string filePath)
        => Path.GetExtension(filePath).Equals(".cdg", StringComparison.OrdinalIgnoreCase);

    /// <summary>A .cdg holds only graphics; its audio is the same-named .mp3 beside it.</summary>
    /// <remarks>Through <see cref="MediaFormats.FindKaraokeAudio"/>, so the importer, the probe and
    /// the renderer all decide a pair the same way.</remarks>
    internal static string? ResolveCompanionAudio(string filePath)
        => IsGraphicsOnly(filePath) ? MediaFormats.FindKaraokeAudio(filePath) : null;

    /// <summary>The painted words, read raw off the pipe, and the picture they go over when the
    /// source has none of its own.</summary>
    private static string BurnInInputs(BurnInOverlay burnIn)
    {
        var inputs = string.Format(
            CultureInfo.InvariantCulture,
            " -f rawvideo -pix_fmt rgba -s {0}x{1} -r {2} -thread_queue_size 64 -i pipe:0",
            burnIn.Width, burnIn.Height, burnIn.FramesPerSecond);

        return burnIn.Base switch
        {
            // Looped for as long as the song lasts; the overlay ends the picture with the words.
            BurnInBase.Background => inputs + $" -stream_loop -1 -i \"{burnIn.BackgroundPath}\"",
            BurnInBase.Fill => inputs + string.Format(
                CultureInfo.InvariantCulture,
                " -f lavfi -i color=c=black:s={0}x{1}:r={2}",
                burnIn.Width, burnIn.Height, burnIn.FramesPerSecond),
            _ => inputs,
        };
    }

    /// <summary>One graph laying the painted frames over the picture, carrying the audio graph with
    /// it when there is one, and the maps that pick both.</summary>
    /// <remarks>The base is always the picture and the words always the overlay: <c>overlay</c> takes
    /// its alpha from the base, so the other way round composites onto transparency and the words
    /// vanish from an encode that still runs cleanly.
    ///
    /// <para>The base is brought to the painted frames' rate, because a source that only emits a
    /// frame when its picture changes would hold the chase still between them. A source's own video
    /// is fitted inside the frame; a background clip covers it, having no framing of its own to
    /// keep. An endless base ends with the words (<c>shortest=1</c>); a finite one ends the picture
    /// itself and carries on without them if the words run out first.</para></remarks>
    private static string BurnInMapping(
        BurnInOverlay burnIn, int pipeInput, int tempo, int audioInput, string mixGraph, string audioFilter,
        bool holdsPictureToTheAudio)
    {
        var size = string.Format(CultureInfo.InvariantCulture, "{0}:{1}", burnIn.Width, burnIn.Height);
        var rate = burnIn.FramesPerSecond.ToString(CultureInfo.InvariantCulture);
        var baseInput = pipeInput + 1;

        var picture = burnIn.Base switch
        {
            BurnInBase.SourceVideo => "[0:v:0]"
                + (holdsPictureToTheAudio ? HoldLastFrame + "," : "")
                + (BuildVideoFilter(tempo) is { Length: > 0 } retime ? retime + "," : "")
                + $"fps={rate},scale={size}:force_original_aspect_ratio=decrease,"
                + $"pad={size}:(ow-iw)/2:(oh-ih)/2,setsar=1[base]",
            BurnInBase.Background => $"[{baseInput}:v]scale={size}:force_original_aspect_ratio=increase,"
                + $"crop={size},setsar=1,fps={rate}[base]",
            _ => $"[{baseInput}:v]setsar=1[base]",
        };

        var ending = burnIn.Base == BurnInBase.SourceVideo ? "eof_action=pass" : "shortest=1";
        var graph = $"{picture};[base][{pipeInput}:v]overlay=0:0:{ending}[v]";

        if (mixGraph.Length > 0)
            return $" -filter_complex \"{graph};{mixGraph}\" -map \"[v]\" -map \"[a]\"";

        // "?" so a source with no sound still encodes its picture rather than failing the map.
        var audio = $" -filter_complex \"{graph}\" -map \"[v]\" -map {audioInput}:a:0?";
        return audioFilter.Length > 0 ? audio + $" -af \"{audioFilter}\"" : audio;
    }

    private static string BuildAudioFilter(int pitch, int tempo)
    {
        var rate = StreamRate.FromTempo(tempo);

        if (pitch == 0 && rate == 1.0) return string.Empty;

        var ratio = Math.Pow(2.0, pitch / 12.0);

        // asetrate reinterprets whatever rate reaches it, so the leading resample is what makes
        // its base true: a 48kHz source would otherwise carry an uncorrected 44100/48000 as well.
        var stages = new List<string> { "aresample=44100" };

        if (pitch != 0)
        {
            stages.Add(FormattableString.Invariant($"asetrate=44100*{ratio:F6}"));
            stages.Add("aresample=44100");
        }

        // One factor, not two: asetrate already moved the speed by the pitch ratio, so undoing
        // that and applying the wanted tempo is a single atempo.
        stages.AddRange(TempoStages(rate / ratio));

        return string.Join(',', stages);
    }

    /// <summary>atempo rejects below 0.5, but pitch-up with tempo-down can reach 0.354 together.</summary>
    /// <remarks>Two chained stages cover the whole envelope.</remarks>
    private static IEnumerable<string> TempoStages(double factor)
    {
        if (Math.Abs(factor - 1.0) < 1e-9) yield break;

        if (factor >= 0.5)
        {
            yield return FormattableString.Invariant($"atempo={factor:F6}");
            yield break;
        }

        var stage = Math.Sqrt(factor);
        yield return FormattableString.Invariant($"atempo={stage:F6}");
        yield return FormattableString.Invariant($"atempo={stage:F6}");
    }

    /// <summary>Balances the named voices over the music; empty when there is nothing to balance.</summary>
    /// <remarks>Leaves the simpler -af path in place.</remarks>
    private static string BuildMixGraph(AudioMix? mix, string audioFilter)
    {
        if (mix is not { IsMixable: true }) return string.Empty;

        var stages = new List<string>();
        var labels = new List<string>();

        foreach (var track in mix.Tracks)
        {
            var volume = mix.VolumeFor(track);

            // Suffixed by index: a duet carries two leads, and a repeated pad label is an ffmpeg error.
            var label = track.Role switch
            {
                AudioTrackRole.Music => "m",
                AudioTrackRole.Lead => "l",
                _ => "b",
            } + track.Index.ToString(CultureInfo.InvariantCulture);

            stages.Add(FormattableString.Invariant(
                $"[0:a:{track.Index}]volume={volume / 100.0:F3}[{label}]"));
            labels.Add($"[{label}]");
        }

        // normalize=0 or amix divides by the number of inputs, quietly dropping the whole mix by
        // several decibels the moment a second track joins.
        stages.Add(FormattableString.Invariant(
            $"{string.Concat(labels)}amix=inputs={labels.Count}:normalize=0[x]"));

        // The pitch and tempo chain rides on the mixed result rather than any one track.
        stages.Add(audioFilter.Length > 0 ? $"[x]{audioFilter}[a]" : "[x]anull[a]");

        return string.Join(';', stages);
    }

    /// <summary>Retimes the picture: output frame rate becomes the source times the rate.</summary>
    /// <remarks>The keyframe expression is immune, since it is written in output time.</remarks>
    /// <summary>Repeats the last frame without end; <c>-shortest</c> is what stops it.</summary>
    private const string HoldLastFrame = "tpad=stop=-1:stop_mode=clone";

    private static string BuildVideoFilter(int tempo)
    {
        var rate = StreamRate.FromTempo(tempo);

        return rate == 1.0
            ? string.Empty
            : FormattableString.Invariant($"setpts=PTS/{rate:F6}");
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
        // Registered under more than one service type, and a container disposes each.
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;

        CloseAllAsync().GetAwaiter().GetResult();
        _lock.Dispose();
    }

    /// <remarks><paramref name="process"/> is null for a session opened without an encode: a served,
    /// swept directory a renderer writes its own files into, with no ffmpeg behind it.</remarks>
    private sealed class Session(string id, string directory, Process? process) : IDisposable
    {
        /// <summary>How long teardown waits for the painters once ffmpeg is gone.</summary>
        private static readonly TimeSpan PainterStopTimeout = TimeSpan.FromSeconds(2);

        private readonly CancellationTokenSource _painting = new();
        private Task? _painter;

        public string Id { get; } = id;
        public string Directory { get; } = directory;

        /// <summary>Feeds ffmpeg the painted words until the song ends or the session closes.</summary>
        public void StartPainting(BurnInPlan plan, Stream pipe, ILogger logger)
        {
            _painter = Task.Run(() =>
            {
                try
                {
                    BurnInFramePump.Run(
                        plan.Painter, pipe, plan.StartSeconds, plan.Rate, plan.Overlay.FramesPerSecond,
                        plan.FrameCount, plan.Workers, _painting.Token);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Painting the words for {SessionId} failed", Id);
                }
            }, CancellationToken.None);
        }

        public void Dispose()
        {
            _painting.Cancel();

            if (process is not null)
            {
                // Killed before the painters are waited on: one blocked writing into the pipe is
                // released only by the reader going away.
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch { /* already gone */ }

                process.Dispose();
            }

            try { _painter?.Wait(PainterStopTimeout); }
            catch { /* its fault is already logged */ }

            _painting.Dispose();

            // A consumer may still hold a segment open; the directory is scratch either way.
            try { System.IO.Directory.Delete(Directory, recursive: true); }
            catch { /* swept on the next start */ }
        }
    }
}
