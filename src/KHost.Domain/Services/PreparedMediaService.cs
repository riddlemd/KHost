using System.Collections.Concurrent;
using FFMpegCore;
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
    private readonly IOptionsMonitor<HlsMediaStreamService.ServiceOptions> _options;
    private readonly IDisposable? _optionsChanged;
    private readonly string _root;

    /// <summary>What a render is called. Not `.mp4`: these are renders of licensed content, and one
    /// that does not open on a double-click is a lower bar to clear than one that does. Obscurity,
    /// not protection, the same as the extension a provider's own renders carry. Nothing reads it
    /// either way, since the muxer is named on both render paths and KHost reads media by content.
    /// </summary>
    private const string RenderExtension = ".khv";

    // One render per file, however many singers queue it: a second caller waits on the first.
    // Lazy, not a bare Task: GetOrAdd may run its factory more than once under contention, and two
    // of these is two ffmpegs writing the same file.
    private readonly ConcurrentDictionary<string, Lazy<Task>> _inFlight = new(StringComparer.Ordinal);

    // One at a time. A host who queues ten songs starts ten renders otherwise, and on the hardware
    // a venue actually runs they would be competing with the song already playing.
    private readonly SemaphoreSlim _renderGate = new(1, 1);

    // One reconcile at a time, with at most one more waiting behind it. Three announcements feed
    // this and a bulk enqueue raises all three per song, so without coalescing a host queueing the
    // night ahead starts hundreds of overlapping passes, each walking the whole queue.
    private readonly SemaphoreSlim _reconcileGate = new(1, 1);
    private int _reconcileQueued;
    private int _noRoomReported;

    // Renders that failed, so one unrenderable file does not take the single render slot on every
    // queue change for the rest of the night while nothing else is ever prepared. Keyed by the
    // destination, which is content addressed, so replacing the source file clears the memo by
    // giving it a different name.
    private readonly ConcurrentDictionary<string, byte> _failed = new(StringComparer.Ordinal);

    // Cancels everything in flight on the way down. Without it a render outlives the host that
    // started it, goes on writing into temp, and competes with the next start.
    private readonly CancellationTokenSource _shutdown = new();

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
        IOptionsMonitor<HlsMediaStreamService.ServiceOptions> options,
        IServiceProvider services,
        IMessageBroker broker)
        : base(logger)
    {
        _options = options;
        _services = services;
        _broker = broker;

        // The directory is resolved once; every knob below it is read live. Moving the root under
        // renders already on disk would orphan them rather than reuse them.
        var working = string.IsNullOrWhiteSpace(Options.WorkingDirectory)
            ? Path.Combine(Path.GetTempPath(), "khost-streams")
            : Options.WorkingDirectory;

        _root = Path.Combine(working, "prepared");

        // Turning it off has to take the renders with it, or the disk it was costing stays spent
        // for the rest of the night. Sweeping while it is already off costs nothing.
        _optionsChanged = options.OnChange(current =>
        {
            // Not a sweep: a sweep takes the renders a plugin's format cannot play without, and
            // the next pass would only build them again. The ordinary reconcile already knows the
            // difference, so it is run with no grace and drops exactly what the setting paid for.
            if (!current.PreRenderQueuedSongs)
                _ = Task.Run(() => ReconcileAsync(TimeSpan.Zero, _shutdown.Token));
        });

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
        StartupReconcile = Task.Run(ReconcileCoalescedAsync);
    }

    /// <summary>Read per use, never snapshotted: these are App Settings a host changes mid-show
    /// and expects to take effect without a restart.</summary>
    private HlsMediaStreamService.ServiceOptions Options => _options.CurrentValue;

    public void Dispose()
    {
        _optionsChanged?.Dispose();
        _subscriptions.Dispose();

        // Cancel before disposing anything a render holds: releasing a disposed semaphore throws
        // out of that render's finally, and an ffmpeg nobody killed keeps writing into temp.
        try
        {
            _shutdown.Cancel();

            // Bounded. A render that will not stop must not hold the host open on the way down.
            Task.WhenAll(_inFlight.Values.Select(WhenSettledAsync)).Wait(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "A render did not stop cleanly on the way down");
        }

        _shutdown.Dispose();
        _renderGate.Dispose();
        _reconcileGate.Dispose();
    }

    /// <summary>The render's task, with its failure already swallowed: this is shutdown, and a
    /// render that threw on the way down is not news.</summary>
    private static Task WhenSettledAsync(Lazy<Task> render)
    {
        try
        {
            return render.Value.ContinueWith(static _ => { }, TaskScheduler.Default);
        }
        catch
        {
            return Task.CompletedTask;
        }
    }

    public Task ReconcileAsync(CancellationToken cancellationToken = default)
        => ReconcileAsync(KeepAfterUnwanted, cancellationToken);

    private async Task ReconcileAsync(TimeSpan grace, CancellationToken cancellationToken)
    {
        var performances = _services.GetService<IPerformanceService>();
        var media = _services.GetService<IMediaService>();

        if (performances is null || media is null)
            return;

        var wanted = new HashSet<string>(StringComparer.Ordinal);

        foreach (var performance in await performances.ReadQueuedAsync())
        {
            if (await media.ReadAsync(performance.MediaId) is { FilePath: { Length: > 0 } path } && File.Exists(path))
                if (NeedsARender(path) && PathFor(path) is { } render)
                    wanted.Add(render);
        }

        // The song at the microphone is no longer queued: playing it dequeued it. Its render is
        // being read by ffmpeg right now, so dropping it cuts the stream off mid-song, which is
        // what this looked like from the room.
        // Kept whatever the setting says: ffmpeg is reading it right now, and releasing disk is
        // never worth cutting off the song in the room.
        if (_services.GetService<IPlaybackService>()?.CurrentMedia?.FilePath is { Length: > 0 } playing
            && File.Exists(playing))
        {
            if (PathFor(playing) is { } playingRender)
                wanted.Add(playingRender);
        }

        // Dropped first, so a long night's renders are not all on the disk at once while the next
        // one is still encoding.
        DiscardAllBut(wanted, grace);

        foreach (var performance in await performances.ReadQueuedAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await media.ReadAsync(performance.MediaId) is { FilePath: { Length: > 0 } path } row)
                await PrepareAsync(path, row.Duration, cancellationToken);
        }
    }

    /// <summary>Everything the queue stopped wanting more than the grace ago. A render in flight is
    /// left alone: it holds no finished file to delete, and its own completion is what puts one
    /// there.</summary>
    private void DiscardAllBut(IReadOnlySet<string> keep, TimeSpan grace)
    {
        try
        {
            var now = DateTime.UtcNow;
            var onDisk = Directory.EnumerateFiles(_root, $"*{RenderExtension}").ToList();

            // A clock for a render that is no longer there is never revisited by the loop below,
            // so it would sit in the dictionary for the life of the process. Cheap to prune, and
            // the sweep and a host deleting a file by hand both leave one behind.
            foreach (var stale in _unwantedSince.Keys.Except(onDisk, StringComparer.Ordinal))
                _unwantedSince.TryRemove(stale, out _);

            foreach (var path in onDisk)
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
                if (now - since < grace)
                    continue;

                // Only when it actually went. A file that would not delete is still there and still
                // wanted by nobody, so reporting it dropped and restarting its clock would hide a
                // directory quietly filling up.
                if (!TryDelete(path))
                    continue;

                _unwantedSince.TryRemove(path, out _);
                Logger.LogInformation("Dropped a render nothing wants");
                _broker.Announce(new PreparedMediaChanged());
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Could not drop unwanted renders");
        }
    }

    private async Task PrepareAsync(string filePath, TimeSpan? expected, CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
            return;

        var preparer = PreparerFor(filePath);

        // Asked per song rather than once per pass, since a host may turn it off while a pass is
        // already walking the queue.
        if (!NeedsARender(filePath, preparer))
            return;

        if (PathFor(filePath) is not { } destination)
            return;

        // Cheapest questions first, and this one is asked of every queued song on every reconcile.
        // Behind a probe it meant an already-rendered song paying two ffprobe spawns, or a whole
        // kit read, every time anybody touched the queue.
        if (File.Exists(destination))
            return;

        // A render that already failed is not retried. One unrenderable file otherwise takes the
        // single render slot on every queue change and nothing else is ever prepared.
        if (_failed.ContainsKey(destination))
            return;

        // A file whose tracks get mixed is never copied, so rendering one is work whose output is
        // thrown away. Worse, the render flattens the stems to one stereo pair, so if it ever were
        // used the host's lead and backing sliders would move nothing. Only for a file the host can
        // already play: one a plugin owns has nothing to fall back to, so it is always rendered.
        if (preparer is null && await IsMixedAtPlaybackAsync(filePath, cancellationToken))
            return;

        // Nothing is free: past the budget, or with the volume nearly full, the song plays the way
        // it always did. A pre-render is an optimisation and must never be why a machine runs out
        // of disk in front of a room.
        if (!HasRoomToRender())
            return;

        // Last, and immediately before anything is written. For licensed content the render is the
        // moment it leaves the provider's container, so a refusal here means no playable copy ever
        // exists, rather than one existing and being refused at the microphone.
        if (!await MayRenderAsync(filePath, expected, cancellationToken))
            return;

        await _inFlight.GetOrAdd(
            destination,
            _ => new Lazy<Task>(
                () => RenderAsync(filePath, destination, preparer, expected, cancellationToken),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    /// <summary>Whether the provider that owns this file will let it be made playable.</summary>
    /// <remarks>Asked of the gate rather than left to the preparer, so a plugin cannot forget it
    /// and every provider is held to the same rule.</remarks>
    private async Task<bool> MayRenderAsync(string filePath, TimeSpan? expected, CancellationToken cancellationToken)
    {
        try
        {
            if (_services.GetService<IMediaGateService>() is not { } gates)
                return true;

            var verdict = await gates.EvaluateAsync(
                MediaAction.Render,
                new Media { FilePath = filePath, Title = Path.GetFileName(filePath), Duration = expected },
                cancellationToken);

            if (!verdict.Allowed)
                Logger.LogInformation("Not rendering '{FilePath}': {Reason}", filePath, verdict.Reason);

            return verdict.Allowed;
        }
        catch (Exception ex)
        {
            // A gate that throws must not stop the rest of the queue being made ready.
            Logger.LogWarning(ex, "Could not ask a provider about rendering '{FilePath}'", filePath);
            return true;
        }
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
    private void Reconcile() => _ = Task.Run(ReconcileCoalescedAsync);

    /// <summary>One reconcile at a time, with at most one more waiting behind it.</summary>
    /// <remarks>Internal so a test can drive the coalescing directly. Reached only through the
    /// broker otherwise, where the announcements are fire and forget and counting passes would be a
    /// race rather than an assertion.</remarks>
    internal async Task ReconcileCoalescedAsync()
    {
        // A pass already waiting will see whatever changed since, so a burst of announcements
        // collapses into one follow-up rather than one pass each.
        if (Interlocked.Exchange(ref _reconcileQueued, 1) == 1)
            return;

        try
        {
            await _reconcileGate.WaitAsync(_shutdown.Token);
        }
        catch (Exception)
        {
            // Shut down while waiting, or the gate is gone. Either way there is nothing to reconcile.
            return;
        }

        try
        {
            // Cleared on the way in, not on the way out: a change arriving while this pass runs
            // has to earn its own follow-up.
            Interlocked.Exchange(ref _reconcileQueued, 0);
            Interlocked.Exchange(ref _noRoomReported, 0);

            await ReconcileAsync(_shutdown.Token);
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Could not bring prepared media in line with the queue");
        }
        finally
        {
            try { _reconcileGate.Release(); } catch (ObjectDisposedException) { }
        }
    }

    public PerformancePreparation StateFor(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return PerformancePreparation.Unprepared;

        if (PathFor(filePath) is not { } destination)
            return PerformancePreparation.Unprepared;

        if (File.Exists(destination))
            return PerformancePreparation.Prepared;

        return _inFlight.ContainsKey(destination)
            ? PerformancePreparation.Preparing
            : PerformancePreparation.Unprepared;
    }

    public bool RequiresPreparation(string filePath) => PreparerFor(filePath) is not null;

    public bool IsWaitingOnARender(string filePath)
        => TryResolve(filePath) is null && RequiresPreparation(filePath);

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
        // The memo goes with the renders it describes. Kept, it would outlive the files it was
        // keyed to and refuse to retry a song whose render was swept rather than failed, which is
        // what turning pre-rendering off and on again would otherwise leave behind.
        _failed.Clear();

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
    /// <remarks>Null when the source cannot be read, which is a file deleted between a caller's
    /// <c>File.Exists</c> and this line. That is a microsecond window and it is reached from a queue
    /// row's render, ahead of the load's own try: thrown from there it takes the Blazor circuit
    /// down, and the row catches only <c>KHostException</c>. A song whose file is gone has no
    /// render, which is what every caller does with the null.</remarks>
    internal string? PathFor(string filePath)
    {
        try
        {
            var info = new FileInfo(filePath);
            var seed = string.Create(CultureInfo.InvariantCulture,
                $"{Path.GetFullPath(filePath)}|{info.Length}|{info.LastWriteTimeUtc.Ticks}");

            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(seed)));

            return Path.Combine(_root, $"{hash}{RenderExtension}");
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Could not name a render for '{FilePath}'", filePath);
            return null;
        }
    }

    /// <inheritdoc />
    /// <remarks>A render this service made carries keyframes on its own segment clock, so it
    /// answers with that. A plugin's render is the plugin's to describe, and a plugin that does
    /// not say leaves the picture to be encoded rather than copied at a cadence nobody checked.
    /// </remarks>
    public int? KeyframeSecondsFor(string filePath)
        => PreparerFor(filePath) is { } preparer
            ? preparer.KeyframeSeconds
            : Math.Max(1, Options.SegmentSeconds);

    /// <summary>Whether this file should have a render at all.</summary>
    /// <remarks>Two different reasons produce one, and only one of them is optional. For a file
    /// the host can already play the render is an optimisation, and <c>PreRenderQueuedSongs</c>
    /// is a host declining to pay for it. For a format only a plugin can read it is the whole of
    /// playability, so the setting does not reach it: turning it off would take such a library
    /// off the menu rather than make it slower. The same distinction the mixable check already
    /// draws a few lines above.</remarks>
    private bool NeedsARender(string filePath, IMediaPreparer? preparer = null)
        => (preparer ?? PreparerFor(filePath)) is not null || Options.PreRenderQueuedSongs;

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

    private async Task RenderAsync(
        string filePath, string destination, IMediaPreparer? preparer, TimeSpan? expected, CancellationToken cancellationToken)
    {
        // Written aside and moved, so a render that dies half way is never resolved as finished.
        var working = destination + ".part";

        // Re-checked here, not only by the caller. Two passes can both read these before either
        // writes one: the in-flight entry is removed when a render ends, so a second pass that
        // looked between the caller's check and this point creates its own entry and renders the
        // same file again. Cheap, and the window widens under load.
        if (_failed.ContainsKey(destination) || File.Exists(destination))
            return;

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
                    _failed[destination] = 0;
                    return;
                }

                if (!await IsWholeAsync(working, expected, filePath, cancellationToken))
                {
                    _failed[destination] = 0;
                    return;
                }

                File.Move(working, destination, overwrite: true);
                Logger.LogInformation("Prepared '{FilePath}'", filePath);
                _broker.Announce(new PreparedMediaChanged());
                return;
            }

            var arguments = BuildArguments(filePath, working, Math.Max(1, Options.SegmentSeconds));

            using var process = Process.Start(new ProcessStartInfo(HlsMediaStreamService.ResolveFfmpeg(), arguments)
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("Failed to start ffmpeg");

            LowerPriority(process);

            // ffmpeg blocks once the stderr pipe fills, so it has to be drained even when discarded.
            var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);

            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Waiting stops at the token; the process does not. Left alone it outlives the host
                // and goes on writing into temp.
                TryKill(process);
                throw;
            }

            if (process.ExitCode != 0 || !File.Exists(working))
            {
                Logger.LogWarning("Could not prepare '{FilePath}': {Error}", filePath, (await stderr).Trim());
                _failed[destination] = 0;
                return;
            }

            if (!await IsWholeAsync(working, expected, filePath, cancellationToken))
            {
                _failed[destination] = 0;
                return;
            }

            File.Move(working, destination, overwrite: true);
            Logger.LogInformation("Prepared '{FilePath}'", filePath);

            // The turn is playable by copy now, and the row showing it has no other way to know.
            _broker.Announce(new PreparedMediaChanged());
        }
        catch (OperationCanceledException)
        {
            // Shutting down, or the queue no longer wants it. Not a failure to remember: the next
            // start should try again.
            throw;
        }
        catch (Exception ex)
        {
            // Never fatal. Failing to prepare costs the song a transcode at play time, which is
            // exactly what happened before any of this existed.
            Logger.LogWarning(ex, "Could not prepare '{FilePath}'", filePath);
            _failed[destination] = 0;
        }
        finally
        {
            try { _renderGate.Release(); } catch (ObjectDisposedException) { }
            _inFlight.TryRemove(destination, out _);
            TryDelete(working);
        }
    }

    /// <summary>Stops an ffmpeg the host has given up waiting for.</summary>
    /// <remarks>Killed rather than closed: it has no console to close, and a render left running
    /// keeps writing a file nothing will ever resolve.</remarks>
    private void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Could not stop a render that was being cancelled");
        }
    }

    /// <summary>Whether a finished render covers the whole song.</summary>
    /// <remarks>A render is named for its source, so a short one is indistinguishable from a whole
    /// one and would be served for the rest of the file's life. Nothing produces a short file today
    /// (a cancelled encode is killed rather than closed, so it never muxes), but the failure is
    /// silent and permanent, and the check costs one probe against work measured in seconds.
    /// </remarks>
    internal static bool IsLongEnough(TimeSpan? expected, TimeSpan actual)
    {
        // Nothing to check against: a row with no duration is not evidence the render is short.
        if (expected is not { TotalSeconds: > 0 } wanted)
            return true;

        // A render can legitimately fall a little short: a .cdg stops drawing before its audio
        // ends, so the picture runs out first. Only a real truncation is worth refusing.
        return actual >= wanted - TimeSpan.FromSeconds(5);
    }

    private async Task<bool> IsWholeAsync(string working, TimeSpan? expected, string filePath, CancellationToken cancellationToken)
    {
        // A row imported without one leaves nothing to measure against, and a render is named for
        // its source, so a short one accepted here is served for the life of that file. The source
        // itself knows how long it is, and for a plugin's own container its own probe answers.
        expected ??= await SourceLengthAsync(filePath, cancellationToken);

        if (expected is null)
        {
            Logger.LogDebug("Nothing to measure the render of '{FilePath}' against", filePath);
            return true;
        }

        try
        {
            var analysis = await FFProbe.AnalyseAsync(working, cancellationToken: cancellationToken);

            if (IsLongEnough(expected, analysis.Duration))
                return true;

            Logger.LogWarning(
                "Discarded a short render of '{FilePath}': {Actual} against {Expected}",
                filePath, analysis.Duration, expected);

            return false;
        }
        catch (Exception ex)
        {
            // A render that cannot be probed is not evidence it is short, and refusing it would
            // cost the song its copy for no reason.
            Logger.LogDebug(ex, "Could not measure the render of '{FilePath}'", filePath);
            return true;
        }
    }

    /// <summary>The pass the constructor starts. Nothing awaits it in the host: the queue is read
    /// from the database rather than from the service that loads it, so there is nothing to be
    /// early for.</summary>
    /// <remarks>Held only so a test can wait for it. Unawaited work that touches the same
    /// substitutes a test is arranging is how three separate tests here came to fail intermittently
    /// and only on a loaded machine.</remarks>
    internal Task StartupReconcile { get; }

    /// <summary>Whether a grace clock is being held against this render. Internal so a test can
    /// watch one being forgotten, which is otherwise invisible: the leak costs nothing observable
    /// until a long night has accumulated thousands of them.</summary>
    internal bool HasGraceClockFor(string renderPath) => _unwantedSince.ContainsKey(renderPath);

    /// <summary>Whether another render fits, by the venue's budget and by the volume's free space.
    /// </summary>
    /// <remarks>Measured per render rather than tracked as a running total: renders arrive and are
    /// dropped from several places, and a counter that drifted would either stall preparation for
    /// the rest of the night or stop capping anything. A directory enumeration costs far less than
    /// the encode it is deciding about.</remarks>
    internal bool HasRoomToRender()
    {
        try
        {
            var budget = (long)Math.Max(0, Options.PreparedBudgetMegabytes) * 1024 * 1024;
            var floor = (long)Math.Max(0, Options.PreparedFreeSpaceFloorMegabytes) * 1024 * 1024;

            if (budget > 0 && HeldBytes() >= budget)
            {
                LogNoRoom("the {Megabytes} MB budget is used up", Options.PreparedBudgetMegabytes);
                return false;
            }

            if (floor > 0 && new DriveInfo(Path.GetPathRoot(_root) ?? _root).AvailableFreeSpace <= floor)
            {
                LogNoRoom("the volume has less than {Megabytes} MB free", Options.PreparedFreeSpaceFloorMegabytes);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            // Not being able to measure is not a reason to stop preparing: the render is the
            // optimisation, and refusing every one of them is the larger regression.
            Logger.LogDebug(ex, "Could not measure the room left for renders");
            return true;
        }
    }

    private long HeldBytes()
        => Directory.Exists(_root)
            ? new DirectoryInfo(_root).EnumerateFiles().Sum(file => file.Length)
            : 0;

    /// <summary>Once per reconcile at most: this is reached per queued song, and a full disk would
    /// otherwise write a line for each of them every time anybody touched the queue.</summary>
    private void LogNoRoom(string because, int megabytes)
    {
        if (Interlocked.Exchange(ref _noRoomReported, 1) == 1)
            return;

        Logger.LogWarning(
            "Not pre-rendering: " + because + ". Songs will transcode at play time instead.",
            megabytes);
    }

    /// <summary>How long the source runs, asked of the probe that understands it.</summary>
    private async Task<TimeSpan?> SourceLengthAsync(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            if (_services.GetService<IMediaProbeService>() is not { } probes)
                return null;

            return (await probes.ProbeAsync(filePath, cancellationToken))?.Duration;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Measuring is a check on the render, not the render itself: failing to measure must
            // not cost the song the copy it just paid for.
            Logger.LogDebug(ex, "Could not measure the source '{FilePath}'", filePath);
            return null;
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

    /// <summary>Whether the file is gone, either because it was deleted or was never there.</summary>
    private bool TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);

            return true;
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Could not remove a partial render at {Path}", path);
            return false;
        }
    }
}
