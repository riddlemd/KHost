using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using KHost.Abstractions.Models;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using KHost.Abstractions.Services;
using KHost.Domain.Services.Screens;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KHost.Domain.Services;

/// <summary>Renders a queued song ahead of time so starting it is a stream copy.</summary>
/// <remarks>Measured on a 230.7s CDG song: transcoding at play time costs 21.6s of wall on slow
/// cores against 0.24s to copy a prepared file, and the transcode lands exactly on the song
/// transition since nothing paces it.</remarks>
public sealed class PreparedMediaService : BaseService, IPreparedMediaService, IStartsWithTheHost, IDisposable
{
    // The stream service's own options, not a second copy: the segment length has to be the
    // same number here and there, or the copy's keyframes land where the segmenter is not cutting.
    private readonly HlsMediaStreamService.ServiceOptions _options;
    private readonly string _root;

    // One render per file, however many singers queue it: a second caller waits on the first.
    private readonly ConcurrentDictionary<string, Task> _inFlight = new(StringComparer.Ordinal);

    // One at a time. A host who queues ten songs starts ten renders otherwise, and on the hardware
    // a venue actually runs they would be competing with the song already playing.
    private readonly SemaphoreSlim _renderGate = new(1, 1);

    /// <summary>How long a render outlives the queue wanting it. A song that has just ended is the
    /// one most likely to be asked for again, and re-rendering a kit costs the room a wait.</summary>
    /// <remarks>Checked when the queue changes, never on a timer: a show moves constantly, so an
    /// expired render is dropped at the next enqueue or dequeue. Five minutes is therefore a
    /// minimum, not a deadline, and the startup sweep is the backstop.</remarks>
    /// <remarks>Settable so a test can watch the eviction it guards without waiting five minutes.
    /// </remarks>
    internal TimeSpan KeepAfterUnwanted { get; init; } = TimeSpan.FromMinutes(5);

    // When each render stopped being wanted. Held here rather than read off the file, whose times
    // say when it was made, not when the queue last had a use for it.
    private readonly ConcurrentDictionary<string, DateTime> _unwantedSince = new(StringComparer.Ordinal);

    // Resolved on use, never in the constructor: PerformanceService reaches this service, so asking
    // for it up front closes a ring the container cannot build.
    private readonly IServiceProvider _services;
    private readonly IMessageBroker _broker;
    private readonly SubscriptionSet _subscriptions = new();

    public PreparedMediaService(
        ILogger<PreparedMediaService> logger,
        IOptions<HlsMediaStreamService.ServiceOptions> options,
        IServiceProvider services,
        IMessageBroker broker)
        : base(logger)
    {
        _options = options.Value;
        _services = services;
        _broker = broker;

        var working = string.IsNullOrWhiteSpace(_options.WorkingDirectory)
            ? Path.Combine(Path.GetTempPath(), "khost-streams")
            : _options.WorkingDirectory;

        _root = Path.Combine(working, "prepared");

        // On the way up, not lazily: a render is only valid against the venue settings and the
        // source file it was made from, and both can change while the host is down.
        Sweep();

        // Enqueue, dequeue and remove all land here, so one rule covers gaining a render and
        // losing one. The queue announces on load too, which is what renders a queue that was
        // already there when the host started.
        _subscriptions.Add(broker.Subscribe<PerformancesChanged>(_ => Reconcile()));
        _subscriptions.Add(broker.Subscribe<SingerQueueChanged>(_ => Reconcile()));

        // A provider enqueues the turn and then fetches the file, so at enqueue there is often
        // nothing on disk to render yet. This is what catches the moment it lands.
        _subscriptions.Add(broker.Subscribe<MediaLibraryChanged>(_ => Reconcile()));

        // Once on the way up, rather than waiting for the queue's own announcement: that is
        // published as the queue loads, which is the same moment this is being constructed, so
        // whether it is heard is a race. The queue is read from the database here, not from the
        // service that loads it, so there is nothing to be early for.
        Reconcile();
    }

    public void Dispose()
    {
        _subscriptions.Dispose();
        _renderGate.Dispose();
    }

    public async Task ReconcileAsync(CancellationToken cancellationToken = default)
    {
        var performances = _services.GetService<IPerformanceService>();
        var media = _services.GetService<IMediaService>();

        if (performances is null || media is null)
            return;

        var wanted = new HashSet<string>(StringComparer.Ordinal);

        foreach (var performance in await performances.ReadQueuedAsync())
        {
            if (await media.ReadAsync(performance.MediaId) is { FilePath: { Length: > 0 } path } && File.Exists(path))
                wanted.Add(PathFor(path));
        }

        // The song at the microphone is no longer queued: playing it dequeued it. Its render is
        // being read by ffmpeg right now, so dropping it cuts the stream off mid-song, which is
        // what this looked like from the room.
        if (_services.GetService<IPlaybackService>()?.CurrentMedia?.FilePath is { Length: > 0 } playing
            && File.Exists(playing))
        {
            wanted.Add(PathFor(playing));
        }

        // Dropped first, so a long night's renders are not all on the disk at once while the next
        // one is still encoding.
        DiscardAllBut(wanted);

        foreach (var performance in await performances.ReadQueuedAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await media.ReadAsync(performance.MediaId) is { FilePath: { Length: > 0 } path })
                await PrepareAsync(path, cancellationToken);
        }
    }

    /// <summary>Everything the queue stopped wanting more than the grace ago. A render in flight is
    /// left alone: it holds no finished file to delete, and its own completion is what puts one
    /// there.</summary>
    private void DiscardAllBut(IReadOnlySet<string> keep)
    {
        try
        {
            var now = DateTime.UtcNow;

            foreach (var path in Directory.EnumerateFiles(_root, "*.mp4"))
            {
                if (keep.Contains(path) || _inFlight.ContainsKey(path))
                {
                    // Wanted again: a song re-queued inside the grace keeps the render it had, so
                    // asking for it a second time costs nothing.
                    _unwantedSince.TryRemove(path, out _);
                    continue;
                }

                var since = _unwantedSince.GetOrAdd(path, now);

                // A song that has just ended is the one most likely to be played again.
                if (now - since < KeepAfterUnwanted)
                    continue;

                TryDelete(path);
                _unwantedSince.TryRemove(path, out _);
                Logger.LogInformation("Dropped a render nothing has wanted for {Minutes} minutes",
                    (int)KeepAfterUnwanted.TotalMinutes);
                _broker.Announce(new PreparedMediaChanged());
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Could not drop unwanted renders");
        }
    }

    private async Task PrepareAsync(string filePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
            return;

        var preparer = PreparerFor(filePath);

        // A file whose tracks get mixed is never copied, so rendering one is work whose output is
        // thrown away. Worse, the render flattens the stems to one stereo pair, so if it ever were
        // used the host's lead and backing sliders would move nothing. Only for a file the host can
        // already play: one a plugin owns has nothing to fall back to, so it is always rendered.
        if (preparer is null && await IsMixedAtPlaybackAsync(filePath, cancellationToken))
            return;

        var destination = PathFor(filePath);

        if (File.Exists(destination))
            return;

        await _inFlight.GetOrAdd(destination, _ => RenderAsync(filePath, destination, preparer, cancellationToken));
    }

    private async Task<bool> IsMixedAtPlaybackAsync(string filePath, CancellationToken cancellationToken)
    {
        if (_services.GetService<IAudioTrackService>() is not { } tracks)
            return false;

        try
        {
            var mix = new AudioMix(
                await tracks.ReadTracksAsync(filePath, cancellationToken),
                AudioMix.DefaultLeadVolume,
                AudioMix.MaxVolume);

            return mix.IsMixable;
        }
        catch (Exception ex)
        {
            // Unreadable tracks are not a reason to skip: the render is still correct for a file
            // that turns out to need no mix, and wasted for one that does.
            Logger.LogDebug(ex, "Could not read the tracks of '{FilePath}'", filePath);
            return false;
        }
    }

    /// <summary>Not awaited by the broker: handlers run one at a time, and a render is minutes of
    /// work that every other subscriber would be queued behind.</summary>
    private void Reconcile() => _ = Task.Run(async () =>
    {
        try
        {
            await ReconcileAsync();
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Could not bring prepared media in line with the queue");
        }
    });

    public PerformancePreparation StateFor(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return PerformancePreparation.Unprepared;

        var destination = PathFor(filePath);

        if (File.Exists(destination))
            return PerformancePreparation.Prepared;

        return _inFlight.ContainsKey(destination)
            ? PerformancePreparation.Preparing
            : PerformancePreparation.Unprepared;
    }

    public bool RequiresPreparation(string filePath) => PreparerFor(filePath) is not null;

    public string? TryResolve(string filePath)
    {
        if (!File.Exists(filePath))
            return null;

        var destination = PathFor(filePath);

        // Never waits on one in flight. A host who plays before the render finishes gets today's
        // transcode, which is slower but correct, rather than a spinner in front of a room.
        return File.Exists(destination) ? destination : null;
    }

    public void Sweep()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);

            Directory.CreateDirectory(_root);
        }
        catch (Exception ex)
        {
            // Leftovers cost disk, not correctness: every name carries its source's size and write
            // time, so a stale render is never resolved for a file that has changed.
            Logger.LogWarning(ex, "Could not clear prepared media");
        }
    }

    /// <summary>Named for the source's path, size and write time together, so editing a file in
    /// place leaves its old render unreachable instead of playing it in the new one's stead.</summary>
    private string PathFor(string filePath)
    {
        var info = new FileInfo(filePath);
        var seed = string.Create(CultureInfo.InvariantCulture,
            $"{Path.GetFullPath(filePath)}|{info.Length}|{info.LastWriteTimeUtc.Ticks}");

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(seed)));

        return Path.Combine(_root, $"{hash}.mp4");
    }

    /// <summary>The plugin that owns this format, or null when the host can read the file itself.
    /// </summary>
    private IMediaPreparer? PreparerFor(string filePath)
    {
        try
        {
            return _services.GetServices<IMediaPreparer>().FirstOrDefault(p => p.CanPrepare(filePath));
        }
        catch (Exception ex)
        {
            // A plugin that throws deciding whether a file is its own must not stop the queue.
            Logger.LogWarning(ex, "A preparer failed on '{FilePath}'", filePath);
            return null;
        }
    }

    private async Task RenderAsync(string filePath, string destination, IMediaPreparer? preparer, CancellationToken cancellationToken)
    {
        // Written aside and moved, so a render that dies half way is never resolved as finished.
        var working = destination + ".part";

        // One encode at a time, so a bulk enqueue does not put every ffmpeg on the machine at once.
        await _renderGate.WaitAsync(cancellationToken);

        try
        {
            Logger.LogInformation("Preparing '{FilePath}'", filePath);
            _broker.Announce(new PreparedMediaChanged());

            if (preparer is not null)
            {
                if (!await preparer.PrepareAsync(filePath, working, cancellationToken) || !File.Exists(working))
                {
                    Logger.LogWarning("A plugin could not prepare '{FilePath}'", filePath);
                    return;
                }

                File.Move(working, destination, overwrite: true);
                Logger.LogInformation("Prepared '{FilePath}'", filePath);
                _broker.Announce(new PreparedMediaChanged());
                return;
            }

            var arguments = BuildArguments(filePath, working, Math.Max(1, _options.SegmentSeconds));

            using var process = Process.Start(new ProcessStartInfo(HlsMediaStreamService.ResolveFfmpeg(), arguments)
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("Failed to start ffmpeg");

            LowerPriority(process);

            // ffmpeg blocks once the stderr pipe fills, so it has to be drained even when discarded.
            var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);

            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0 || !File.Exists(working))
            {
                Logger.LogWarning("Could not prepare '{FilePath}': {Error}", filePath, (await stderr).Trim());
                return;
            }

            File.Move(working, destination, overwrite: true);
            Logger.LogInformation("Prepared '{FilePath}'", filePath);

            // The turn is playable by copy now, and the row showing it has no other way to know.
            _broker.Announce(new PreparedMediaChanged());
        }
        catch (Exception ex)
        {
            // Never fatal. Failing to prepare costs the song a transcode at play time, which is
            // exactly what happened before any of this existed.
            Logger.LogWarning(ex, "Could not prepare '{FilePath}'", filePath);
        }
        finally
        {
            _renderGate.Release();
            _inFlight.TryRemove(destination, out _);
            TryDelete(working);
        }
    }

    /// <summary>Encoded to what the segmenter would have produced, so the copy is a real copy: the
    /// same keyframe cadence, or HLS cannot cut segments where it needs them.</summary>
    internal static string BuildArguments(string filePath, string destination, int segmentSeconds)
    {
        var arguments = $"-hide_banner -loglevel error -i \"{filePath}\"";

        // A .cdg carries no audio at all; its sound is the same-named .mp3 beside it.
        if (HlsMediaStreamService.ResolveCompanionAudio(filePath) is { } companion)
            arguments += $" -i \"{companion}\" -map 0:v:0 -map 1:a:0";

        // A .cdg only emits a frame when the graphics change, so a forced keyframe lands on the
        // next sparse frame rather than on the boundary asked for. Copying can then only cut where
        // those keyframes fell, which measured as a zero-length segment and a nine-second one.
        // A constant rate puts them where the segmenter wants them, and costs nothing: the render
        // came out smaller and faster than without it.
        if (HlsMediaStreamService.IsGraphicsOnly(filePath))
            arguments += " -r 30";

        arguments += " -c:v libx264 -preset veryfast -profile:v main -level 4.1 -pix_fmt yuv420p"
                   + string.Format(
                       CultureInfo.InvariantCulture,
                       " -force_key_frames \"expr:gte(t,n_forced*{0})\" -sc_threshold 0",
                       segmentSeconds)
                   + " -c:a aac -ar 44100 -ac 2 -b:a 128k"
                   // The muxer is named rather than inferred: the render is written to a .part
                   // name so a half-finished one is never resolved, and ffmpeg reads the format
                   // off the extension.
                   + $" -movflags +faststart -f mp4 -y \"{destination}\"";

        return arguments;
    }

    /// <summary>Below the song that is playing. The whole point is to spend time the room is not
    /// waiting on, so a render must never compete with what it is rendering behind.</summary>
    private void LowerPriority(Process process)
    {
        try
        {
            process.PriorityClass = ProcessPriorityClass.BelowNormal;
        }
        catch (Exception ex)
        {
            // Not every platform lets a process lower its child's priority, and a render at normal
            // priority is still worth having.
            Logger.LogDebug(ex, "Could not lower the priority of a prepare");
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Could not remove a partial render at {Path}", path);
        }
    }
}
