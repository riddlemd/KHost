using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using FFMpegCore;
using KHost.Abstractions.Exceptions;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using KHost.Common.Media;
using KHost.Domain.Services.BurnIn;
using KHost.Domain.Services.FFmpeg;
using KHost.Domain.Services.VideoEncoding;

namespace KHost.Domain.Services;

/// <summary>One ffmpeg run feeds any number of consumers, all of them plain HTTP clients.</summary>
public sealed class HlsMediaStreamService : BaseService, IMediaStreamService, IBurnInStreamService, IStemStreamService, IDisposable
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

        /// <summary>The frame height a .cdg is scaled into, and the size of every burned-in frame;
        /// one of <see cref="GraphicsScaling.Heights"/>.</summary>
        public int GraphicsScaleHeight { get; set; } = GraphicsScaling.DefaultHeight;

        /// <summary>Whether video is cut on a hardware H.264 encoder; read per song.</summary>
        public VideoEncoderPreference Encoder { get; set; } = VideoEncoderPreference.Auto;
    }

    internal const string FfmpegMissingCode = "KH-FFMPEG-MISSING";

    /// <summary>Generous: a first segment normally lands in well under a second.</summary>
    private static readonly TimeSpan PlaylistTimeout = TimeSpan.FromSeconds(15);

    private readonly IOptionsMonitor<ServiceOptions> _options;
    // Read without _lock on every segment and playlist GET; every write still takes _lock.
    private readonly ConcurrentDictionary<string, Session> _sessions = new();
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly IPlayableMediaSourceService _playableSources;
    private readonly IFFmpegService _ffmpeg;
    private readonly TimeProvider _time;
    private readonly IVideoEncoderSelector _encoders;
    private readonly string _root;
    private int _disposed;

    public HlsMediaStreamService(
        ILogger<HlsMediaStreamService> logger,
        IOptionsMonitor<ServiceOptions> options,
        IPlayableMediaSourceService playableSources,
        TimeProvider time,
        IFFmpegService ffmpeg,
        IVideoEncoderSelector encoders)
        : base(logger)
    {
        _options = options;
        _playableSources = playableSources;
        _time = time;
        _ffmpeg = ffmpeg;
        _encoders = encoders;

        // The root is resolved once and the rest is read live. Moving the directory under running
        // sessions would strand the segments they are already serving, where a changed segment
        // length only decides how the next stream is cut.
        var working = string.IsNullOrWhiteSpace(Options.WorkingDirectory)
            ? Path.Combine(Path.GetTempPath(), "khost-streams")
            : Options.WorkingDirectory;

        _root = working;

        Directory.CreateDirectory(_root);

        // Before this host opens any session of its own, so only another process's folders are judged.
        var swept = HlsSessionSweeper.Sweep(_root, _time.GetUtcNow().UtcDateTime, HlsSessionSweeper.IsRunning);
        if (swept.Count > 0)
            Logger.LogInformation("Swept {Count} orphaned stream session(s) from {Root}: {Sessions}",
                swept.Count, _root, string.Join(", ", swept));
    }

    /// <summary>Read per use, never snapshotted: a host changing the segment length in App
    /// Settings expects the next song to honour it, not the next launch.</summary>
    private ServiceOptions Options => _options.CurrentValue;

    /// <summary>Where every URL this service hands out starts, read live with the base address.</summary>
    private string MediaPrefix => $"{Options.BaseAddress.TrimEnd('/')}/media/";

    /// <summary>A fresh session's id and scratch directory, shared by both open paths.</summary>
    private (string Id, string Directory) NewSession()
    {
        var id = Guid.NewGuid().ToString("n");
        var directory = Path.Combine(_root, id);
        Directory.CreateDirectory(directory);
        HlsSessionSweeper.WriteOwner(directory);

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
        => OpenEncodeAsync(filePath, startOffset, pitch, tempo, mix, words: null, displayDrawsWords: false, cancellationToken);

    public Task<MediaStreamSession> OpenBurningInAsync(
        string filePath,
        TimeSpan startOffset,
        int pitch,
        int tempo,
        AudioMix? mix,
        TimedLyrics words,
        CancellationToken cancellationToken = default)
        => OpenEncodeAsync(filePath, startOffset, pitch, tempo, mix, words, displayDrawsWords: false, cancellationToken);

    public Task<MediaStreamSession> OpenUnderDrawnWordsAsync(
        string filePath,
        TimeSpan startOffset,
        int pitch,
        int tempo,
        AudioMix? mix,
        CancellationToken cancellationToken = default)
        => OpenEncodeAsync(filePath, startOffset, pitch, tempo, mix, words: null, displayDrawsWords: true, cancellationToken);

    private async Task<MediaStreamSession> OpenEncodeAsync(
        string filePath,
        TimeSpan startOffset,
        int pitch,
        int tempo,
        AudioMix? mix,
        TimedLyrics? words,
        bool displayDrawsWords,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Media file not found: {filePath}", filePath);

        var (id, directory) = NewSession();

        // Into the session's own directory, so a converted copy is swept with the segments when
        // the session closes and nothing has to remember it exists.
        var source = await _playableSources.ResolvePlayableAsync(filePath, directory, cancellationToken);

        // Everything below reads the resolved path: a companion audio file sits beside the original, but
        // what ffmpeg opens, and what decides the graphics-only frame rate, is what it will read.
        var companionAudio = HlsArguments.ResolveCompanionAudio(source);
        if (companionAudio is null && HlsArguments.IsGraphicsOnly(source))
            Logger.LogWarning("No companion audio beside '{FilePath}'; the stream will be silent", source);

        // Read once per open, so a change made mid-song applies from the next song or reopen.
        var graphicsHeight = GraphicsScaling.SnapToOffered(Options.GraphicsScaleHeight);

        var picture = words is null ? default : await ProbePictureAsync(source, cancellationToken);
        BurnInPlan? BurnInAt(TimeSpan at) => words is null
            ? null
            : PlanBurnIn(source, picture.HasVideo, picture.DurationSeconds, HlsArguments.IsGraphicsOnly(source), words, at, tempo, graphicsHeight);

        var burnIn = BurnInAt(startOffset);
        var segmentSeconds = Options.SegmentSeconds;
        var songTime = SongTimeAfter(startOffset, tempo);

        var inputs = new EncodeInputs(source, startOffset, pitch, tempo, segmentSeconds)
        {
            CompanionAudioPath = companionAudio,
            Mix = mix,
            BurnIn = burnIn?.Overlay,
            GraphicsHeight = graphicsHeight,
            DisplayDrawsWords = displayDrawsWords,
        };

        var recipe = new EncodeRecipe(
            (encoder, part) => HlsArguments.BuildArguments(
                inputs with { StartOffset = songTime(part.After) }, encoder, part),
            after => after == TimeSpan.Zero ? burnIn : BurnInAt(songTime(after)),
            HlsArguments.HasPicture(inputs));

        Logger.LogInformation(
            "Opening stream {SessionId} for '{FilePath}' at {Offset}{BurnIn}",
            id, source, startOffset, burnIn is null ? "" : $", words burned in over {burnIn.Overlay.Base}");

        return await StartEncodeAsync(
            id, directory, filePath, recipe, startOffset, pitch, tempo, adopted: null, cancellationToken);
    }

    public async Task<MediaStreamSession> OpenStemsAsync(
        string sourcePath,
        IReadOnlyList<StemSource> stems,
        TimeSpan startOffset,
        int pitch,
        int tempo,
        TimedLyrics? words,
        MediaStreamSession? adopt,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (stems.Count == 0)
                throw new InvalidOperationException($"No stems to encode for '{sourcePath}'.");

            var inputs = stems
                .Select(stem => new StemInput(ResolveStemInput(stem.Url), stem.Role, stem.Volume))
                .ToList();

            var (id, directory) = NewSession();

            // A stem set has no picture of its own, so the words run for as long as the longest stem.
            var burnsIn = words is { Pages.Count: > 0 };
            var longest = burnsIn ? await LongestDurationAsync(inputs, cancellationToken) : 0;
            var graphicsHeight = GraphicsScaling.SnapToOffered(Options.GraphicsScaleHeight);
            BurnInPlan? BurnInAt(TimeSpan at) => burnsIn
                ? PlanBurnIn(sourcePath, hasVideo: false, longest, isGraphicsOnly: false, words!, at, tempo, graphicsHeight)
                : null;

            var burnIn = BurnInAt(startOffset);
            var segmentSeconds = Options.SegmentSeconds;
            var songTime = SongTimeAfter(startOffset, tempo);

            // A stem set has no picture of its own: only burned-in words bring one.
            var recipe = new EncodeRecipe(
                (encoder, part) => HlsArguments.BuildStemArguments(
                    inputs, songTime(part.After), pitch, tempo, segmentSeconds, burnIn?.Overlay, encoder, part),
                after => after == TimeSpan.Zero ? burnIn : BurnInAt(songTime(after)),
                HasPicture: burnIn is not null);

            Logger.LogInformation(
                "Opening stream {SessionId} from {Count} stems of '{FilePath}' at {Offset}{BurnIn}",
                id, inputs.Count, sourcePath, startOffset,
                burnIn is null ? "" : $", words burned in over {burnIn.Overlay.Base}");

            return await StartEncodeAsync(
                id, directory, sourcePath, recipe, startOffset, pitch, tempo, adopt?.Id, cancellationToken,
                mixedStems: stems);
        }
        catch
        {
            // Owned from the call on: a caller whose encode never started has nothing to close it by.
            if (adopt is not null) await CloseAsync(adopt.Id);
            throw;
        }
    }

    /// <summary>A local path for a stem written into one of this service's sessions, else the http
    /// address itself for ffmpeg to fetch.</summary>
    /// <remarks>Read off disk where it can be: fetching our own server over loopback only adds a hop.</remarks>
    public string ResolveStemInput(string url)
    {
        var prefix = MediaPrefix;

        if (url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            var parts = url[prefix.Length..].Split('/');

            return parts.Length == 2 && ResolveArtifact(parts[0], Uri.UnescapeDataString(parts[1])) is { } path
                ? path
                : throw new InvalidOperationException($"The stem '{url}' names a session file that is not there.");
        }

        // Quoted onto ffmpeg's command line, where a quote inside would end the argument early.
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && !url.Contains('"'))
            return url;

        throw new InvalidOperationException($"The stem '{url}' is neither a session file nor an http address.");
    }

    public IReadOnlyList<StemSource>? StemsMixedInto(string streamUrl)
    {
        var prefix = MediaPrefix;
        if (!streamUrl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;

        var parts = streamUrl[prefix.Length..].Split('/');
        if (parts.Length != 2) return null;

        return _sessions.TryGetValue(parts[0], out var session) ? session.MixedStems : null;
    }

    /// <remarks>A stem that cannot be probed counts as zero: the words then last as long as they run.</remarks>
    private static async Task<double> LongestDurationAsync(IReadOnlyList<StemInput> inputs, CancellationToken cancellationToken)
    {
        var durations = await Task.WhenAll(inputs.Select(async input =>
        {
            try
            {
                return (await FFProbe.AnalyseAsync(input.Input, cancellationToken: cancellationToken)).Duration.TotalSeconds;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return 0;
            }
        }));

        return durations.Max();
    }

    /// <summary>Everything needed to run a song's encode again: on another encoder, or picking the
    /// playlist up part way.</summary>
    /// <param name="Arguments">The command line on an encoder, for a place in the playlist.</param>
    /// <param name="BurnInAfter">The words to paint into a run that starts after that much output
    /// is listed; null when none are burned in.</param>
    /// <param name="HasPicture">Whether the encode cuts any video, and so has an encoder to choose.</param>
    private sealed record EncodeRecipe(
        Func<VideoEncoderProfile, PlaylistPart, string> Arguments, Func<TimeSpan, BurnInPlan?> BurnInAfter,
        bool HasPicture);

    /// <summary>Where in the song a run starts once <c>after</c> of output is already listed.</summary>
    /// <remarks>Output runs at the tempo against the song, so listed output is that much more song.</remarks>
    internal static Func<TimeSpan, TimeSpan> SongTimeAfter(TimeSpan startOffset, int tempo)
    {
        var rate = StreamRate.FromTempo(tempo);
        return after => startOffset + after * rate;
    }

    /// <summary>Starts ffmpeg in a session directory, registers it, and waits for a playlist worth
    /// handing out.</summary>
    /// <param name="adopted">A session closed along with this one: the files it reads from.</param>
    private async Task<MediaStreamSession> StartEncodeAsync(
        string id, string directory, string filePath, EncodeRecipe recipe,
        TimeSpan startOffset, int pitch, int tempo, string? adopted, CancellationToken cancellationToken,
        IReadOnlyList<StemSource>? mixedStems = null)
    {
        var chosen = await _encoders.SelectAsync(Options.Encoder, cancellationToken);

        // Audio alone names no encoder, so there is no hardware run to fall back from.
        var encoder = chosen.IsHardware && recipe.HasPicture ? chosen : VideoEncoderProfile.Software;

        var started = await TryStartAsync(id, directory, recipe, encoder, adopted, mixedStems, cancellationToken);

        if (!started && encoder.IsHardware)
        {
            Logger.LogWarning(
                "{Codec} produced no stream for {SessionId}; retrying it on libx264", encoder.Codec, id);

            await DiscardAttemptAsync(id, directory);
            started = await TryStartAsync(
                id, directory, recipe, VideoEncoderProfile.Software, adopted, mixedStems, cancellationToken);

            // Blamed only when software then works: a source that cannot be read fails both.
            if (started) _encoders.ReportFailure(encoder);
        }

        if (!started)
        {
            await CloseAsync(id);
            throw new InvalidOperationException(
                $"ffmpeg produced no playlist for '{filePath}'. See the warning logged for session {id}.");
        }

        return new MediaStreamSession
        {
            Id = id,
            SourcePath = filePath,
            PlaylistUrl = $"{MediaPrefix}{id}/{HlsPlaylistFile.FileName}",
            StartOffset = startOffset,
            Pitch = pitch,
            Tempo = tempo,
        };
    }

    /// <summary>Whether ffmpeg, started under <paramref name="id"/>, wrote a playlist worth handing out.</summary>
    /// <remarks>A hardware run leaves the end marker to the host, which then has the choice of
    /// writing it or carrying the song on in software if the run dies.</remarks>
    private async Task<bool> TryStartAsync(
        string id, string directory, EncodeRecipe recipe, VideoEncoderProfile encoder, string? adopted,
        IReadOnlyList<StemSource>? mixedStems, CancellationToken cancellationToken)
    {
        Session session;

        try
        {
            session = Launch(
                id, directory, recipe, encoder, new PlaylistPart(TimeSpan.Zero, LeftOpen: encoder.IsHardware),
                adopted, mixedStems);
        }
        catch (KHostException)
        {
            // Nothing registered the session yet, so nothing else will sweep its folder.
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
            throw;
        }

        var served = false;

        try
        {
            // Registered with a token that never cancels: a caller giving up between Start and here
            // must still find the process in _sessions, or nothing ever tears it down and it runs
            // until the app exits rather than until the next CloseAsync/CloseAllAsync.
            await RegisterAsync(session, CancellationToken.None);

            // A URL handed out early 404s, which a media element reports as "source not supported"
            // and never retries.
            served = await WaitForPlaylistAsync(directory, session, cancellationToken);
            return served;
        }
        catch (OperationCanceledException)
        {
            // The session is already registered above, so a caller who gives up mid-wait still
            // gets the process killed and the directory swept instead of it outliving this call.
            await CloseAsync(id);
            throw;
        }
        finally
        {
            session.SettleServed(served);
        }
    }

    /// <summary>Starts ffmpeg for one run of a song, with its painter, its stderr drained and its
    /// exit watched; registers nothing.</summary>
    private Session Launch(
        string id, string directory, EncodeRecipe recipe, VideoEncoderProfile encoder, PlaylistPart part,
        string? adopted, IReadOnlyList<StemSource>? mixedStems, VideoEncoderProfile? tookOverFrom = null)
    {
        var arguments = recipe.Arguments(encoder, part);
        var burnIn = recipe.BurnInAfter(part.After);

        Logger.LogDebug("ffmpeg {Arguments}", arguments);

        var ffmpegPath = ResolveFfmpegPath() ?? throw FfmpegMissing(null);
        Process process;

        try
        {
            process = Process.Start(new ProcessStartInfo(ffmpegPath, arguments)
            {
                WorkingDirectory = directory,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardInput = burnIn is not null,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("Failed to start ffmpeg");
        }
        catch (Win32Exception ex)
        {
            // Found but not startable (deleted since, or not executable): the same fix applies.
            throw FfmpegMissing(ex);
        }

        var session = new Session(id, directory, process)
        {
            AdoptedSessionId = adopted,
            MixedStems = mixedStems,
            Encoder = encoder,
            Recipe = recipe,
            TookOverFrom = tookOverFrom,
        };

        if (burnIn is not null) session.StartPainting(burnIn, process.StandardInput.BaseStream, Logger);

        // ffmpeg blocks once the stderr pipe fills, so it has to be drained even when discarded.
        _ = Task.Run(async () =>
        {
            var text = await process.StandardError.ReadToEndAsync(CancellationToken.None);
            if (!string.IsNullOrWhiteSpace(text))
                Logger.LogWarning("ffmpeg for {SessionId}: {Error}", id, text.Trim());
        }, CancellationToken.None);

        _ = OnEncodeExitAsync(session, process);

        return session;
    }

    /// <summary>Stops a failed attempt and clears what it wrote, keeping the directory: a converted
    /// source lives in it, and the adopted session is still wanted by the retry.</summary>
    private async Task DiscardAttemptAsync(string id, string directory)
    {
        Session? failed;

        await _lock.WaitAsync();
        try { _sessions.TryRemove(id, out failed); }
        finally { _lock.Release(); }

        failed?.Stop();

        foreach (var file in Directory.EnumerateFiles(directory, "seg_*.ts").Append(Path.Combine(directory, HlsPlaylistFile.FileName)))
        {
            try { File.Delete(file); } catch { /* overwritten by the retry */ }
        }
    }

    /// <summary>What the words go over and how many frames of them to paint.</summary>
    /// <remarks>Whatever <see cref="SongBackdrops.ForPlaying"/> answers, so a burned-in display and a
    /// screen drawing its own words agree. A cover image stored as a video stream is not a picture
    /// to play under the words — it is one frame — and nothing in an audio file is.
    ///
    /// <para>Painted from the playhead, except for a source that seeks on its output: ffmpeg then
    /// discards everything before the playhead, painted frames included.</para></remarks>
    /// <param name="hasVideo">Whether the source carries a video stream that is not an attached picture.</param>
    internal static BurnInPlan PlanBurnIn(
        string sourcePath, bool hasVideo, double duration, bool isGraphicsOnly, TimedLyrics words,
        TimeSpan startOffset, int tempo, int graphicsHeight)
    {
        var basePicture = SongBackdrops.ForPlaying(hasTimedLyrics: true, sourcePath, hasMovingPicture: hasVideo) switch
        {
            SongBackdrop.OwnPicture => BurnInBase.SourceVideo,
            _ => BurnInBase.Fill,
        };

        var (width, height) = BurnInOverlay.FrameFor(graphicsHeight);
        var overlay = new BurnInOverlay(width, height, BurnInOverlay.DefaultFramesPerSecond, basePicture);

        var painter = new TimedLyricsPainter(words, overlay.Width, overlay.Height);

        var start = isGraphicsOnly ? 0 : startOffset.TotalSeconds;
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
            var hasVideo = analysis.VideoStreams.Any(FfprobeSourcePictureProbe.IsMovingPicture);

            return (hasVideo, analysis.Duration.TotalSeconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogWarning(ex, "Could not probe '{FilePath}' for a picture; burning the words in over black", source);
            return (false, 0);
        }
    }

    /// <summary>The file appears before a segment is listed in it, so existing is not playable.</summary>
    /// <remarks>Gives up as soon as ffmpeg has exited without one, so a failed start costs no timeout.</remarks>
    private async Task<bool> WaitForPlaylistAsync(string directory, Session session, CancellationToken cancellationToken)
    {
        var playlist = Path.Combine(directory, HlsPlaylistFile.FileName);
        var deadline = _time.GetUtcNow() + PlaylistTimeout;

        while (_time.GetUtcNow() < deadline)
        {
            // Read before the file, so a short song that finished between the two still counts.
            var exited = session.HasExited;

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

            if (exited) return false;

            await Task.Delay(25, cancellationToken);
        }

        return false;
    }

    /// <summary>Settles a served playlist once its ffmpeg is gone: finishes a rename ffmpeg could
    /// not, carries a hardware run that died on in software, and ends a playlist left open.</summary>
    /// <remarks>ffmpeg writes each update to a .tmp and renames it into place without retrying. On
    /// Windows a virus scanner or indexer opening the playlist at that moment fails the rename, and the
    /// song's last segments and end marker never appear, so a player stalls short of the end.</remarks>
    private async Task OnEncodeExitAsync(Session session, Process process)
    {
        try
        {
            var processId = process.Id;
            await process.WaitForExitAsync(CancellationToken.None);

            // A start that never served is its caller's to retry or close.
            if (!await session.Served) return;

            // Only for the encode still behind the session: a closed one's folder is going, and a
            // later encode in the same folder (a retry or a continuation) is writing its own .tmp there.
            if (await EncoderProcessIdAsync(session.Id) != processId) return;

            int exitCode;
            try { exitCode = process.ExitCode; }
            catch (InvalidOperationException) { return; /* closed just now, which disposed it */ }

            if (await HlsPlaylistFile.CompletePlaylistAsync(session.Directory))
                Logger.LogWarning("ffmpeg could not put the last playlist for {SessionId} in place; the host moved it", session.Id);

            if (exitCode != 0 && session.Encoder.IsHardware)
            {
                await ContinueOnSoftwareAsync(session, exitCode);
                return;
            }

            if (exitCode == 0 && session.TookOverFrom is { } failed) _encoders.ReportFailure(failed);

            if (session.Encoder.IsHardware && await HlsPlaylistFile.EndPlaylistAsync(session.Directory))
                Logger.LogDebug("Ended the playlist for {SessionId}", session.Id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Logger.LogWarning(ex, "Could not finish the playlist for {SessionId}", session.Id);
        }
    }

    /// <summary>Carries a song whose hardware encode died on in libx264, appending to the playlist a
    /// display is already playing from the end of what it lists.</summary>
    /// <remarks>The new run numbers on from the last listed segment, offsets its timestamps to follow
    /// them, and is marked a discontinuity, as its stream parameters differ. Its keyframes are forced
    /// from its own start, which is a segment boundary, so the segments stay on the clock.</remarks>
    private async Task ContinueOnSoftwareAsync(Session failed, int exitCode)
    {
        var listed = HlsPlaylistFile.ListedSegmentSeconds(await File.ReadAllTextAsync(Path.Combine(failed.Directory, HlsPlaylistFile.FileName)));
        var after = TimeSpan.FromSeconds(listed.Sum());

        Logger.LogWarning(
            "{Codec} stopped {SessionId} after {After} (exit {ExitCode}); carrying on with libx264",
            failed.Encoder.Codec, failed.Id, after, exitCode);

        Session next;

        try
        {
            next = Launch(
                failed.Id, failed.Directory, failed.Recipe!, VideoEncoderProfile.Software,
                new PlaylistPart(after, LeftOpen: false), failed.AdoptedSessionId, failed.MixedStems,
                tookOverFrom: failed.Encoder);
        }
        catch (KHostException ex)
        {
            Logger.LogWarning(ex, "Could not carry {SessionId} on in libx264; the song ends early", failed.Id);
            await HlsPlaylistFile.EndPlaylistAsync(failed.Directory);
            return;
        }

        bool current;

        await _lock.WaitAsync();
        try
        {
            current = _sessions.TryGetValue(failed.Id, out var registered) && ReferenceEquals(registered, failed);
            if (current) _sessions[failed.Id] = next;
        }
        finally { _lock.Release(); }

        // Closed while the new run started: nothing will serve or sweep it.
        if (!current)
        {
            next.SettleServed(false);
            next.Stop();
            return;
        }

        next.SettleServed(true);

        // Its process is gone already; this releases the painter still feeding the dead pipe.
        failed.Stop();
    }

    /// <summary>The id of the ffmpeg behind a session, or null for none: lets a test watch the
    /// encode go without asking the OS for command lines, which only some platforms answer.</summary>
    internal async Task<int?> EncoderProcessIdAsync(string sessionId)
    {
        await _lock.WaitAsync();
        try { return _sessions.TryGetValue(sessionId, out var session) ? session.ProcessId : null; }
        finally { _lock.Release(); }
    }

    public async Task CloseAsync(string sessionId)
    {
        Session? session;

        await _lock.WaitAsync();
        try
        {
            if (!_sessions.TryRemove(sessionId, out session)) return;
        }
        finally { _lock.Release(); }

        Logger.LogInformation("Closing stream {SessionId}", sessionId);
        session!.Dispose();

        if (session.AdoptedSessionId is { } adopted) await CloseAsync(adopted);
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
        => $"{MediaPrefix}image/{mediaId}";

    public string BuildArtifactUrl(string sessionId, string fileName)
        => $"{MediaPrefix}{sessionId}/{fileName}";

    public string? ResolveArtifact(string sessionId, string fileName)
    {
        // Anything that is not a bare file name is rejected before it reaches the filesystem.
        if (string.IsNullOrWhiteSpace(fileName)
            || fileName.Contains('/') || fileName.Contains('\\')
            || fileName.Contains("..", StringComparison.Ordinal)
            || Path.IsPathRooted(fileName))
            return null;

        // A close removes the session before deleting its folder, so a racing lookup finds no
        // session, or a folder whose files may already be going.
        if (!_sessions.TryGetValue(sessionId, out var session)) return null;

        var path = Path.Combine(session.Directory, fileName);
        return File.Exists(path) ? path : null;
    }

    /// <summary>Looked for afresh per song, so a copy installed or configured mid-show is used by
    /// the next one without a restart.</summary>
    private string? ResolveFfmpegPath() => _ffmpeg.Locate(FFmpegTool.FFmpeg);

    private static KHostException FfmpegMissing(Exception? inner) => new(
        "KHost cannot find FFmpeg, so it cannot play this song.",
        "Open App Settings and choose Install FFmpeg, or set the FFmpeg directory to a folder that has ffmpeg in it.",
        FfmpegMissingCode,
        inner);

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
        private readonly TaskCompletionSource<bool> _served = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Task? _painter;
        private int _stopped;

        public string Id { get; } = id;
        public string Directory { get; } = directory;

        public int? ProcessId { get; } = process?.Id;

        /// <summary>Another session this one reads from, closed after it.</summary>
        public string? AdoptedSessionId { get; init; }

        /// <summary>The stems this session's encode mixed, when it mixed any.</summary>
        public IReadOnlyList<StemSource>? MixedStems { get; init; }

        /// <summary>The encoder this run's ffmpeg cuts the picture with.</summary>
        public VideoEncoderProfile Encoder { get; init; } = VideoEncoderProfile.Software;

        /// <summary>How to run the song again, kept so a hardware run that dies can be carried on.</summary>
        public EncodeRecipe? Recipe { get; init; }

        /// <summary>The hardware encoder this run took the song over from, blamed once it finishes.</summary>
        public VideoEncoderProfile? TookOverFrom { get; init; }

        /// <summary>Whether this run's playlist is being served; false for a start that failed.</summary>
        public Task<bool> Served => _served.Task;

        /// <summary>True once ffmpeg is gone, including after the session stopped it.</summary>
        public bool HasExited
        {
            get
            {
                try { return process?.HasExited ?? true; }
                catch (InvalidOperationException) { return true; }
            }
        }

        public void SettleServed(bool served) => _served.TrySetResult(served);

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

        /// <summary>Stops the encode and its painters, leaving the directory.</summary>
        public void Stop()
        {
            if (Interlocked.Exchange(ref _stopped, 1) == 1) return;

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
        }

        public void Dispose()
        {
            Stop();

            // A consumer may still hold a segment open; the directory is scratch either way.
            try { System.IO.Directory.Delete(Directory, recursive: true); }
            catch { /* swept on the next start */ }
        }
    }
}
