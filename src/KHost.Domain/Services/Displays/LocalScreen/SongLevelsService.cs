using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KHost.Domain.Services.Displays.LocalScreen;

/// <summary>Reads the playing song's <see cref="SongLevels"/> in the background and holds them for the
/// screen to fetch.</summary>
/// <remarks>Host plumbing for the local screen's visualiser, not a contract. One song at a time:
/// starting the next song's read, or <see cref="Clear"/>, drops the last one. Held apart from the
/// stream sessions on purpose — a key change closes the song's session and opens another, while the
/// levels are indexed by song time and stay right for the whole song.</remarks>
public interface ISongLevelsService
{
    /// <summary>Starts reading and returns the URL the levels will be served at. Returns at once; a
    /// fetch of that URL waits for the read.</summary>
    string Begin(IReadOnlyList<SongLevelsInput> inputs);

    /// <summary>The encoded track once read; null for a token that is not the current song's, or a
    /// read that failed.</summary>
    Task<byte[]?> ReadAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>Drops the current song's levels, stopping a read still running.</summary>
    void Clear();
}

/// <inheritdoc />
public sealed class FfmpegSongLevelsService(
    ILogger<FfmpegSongLevelsService> logger,
    IOptionsMonitor<HlsMediaStreamService.ServiceOptions> options) : ISongLevelsService, IDisposable
{
    /// <summary>Under the host's media surface, beside <c>/media/image</c>.</summary>
    public const string RoutePrefix = "/media/levels/";

    private readonly Lock _gate = new();
    private Entry? _current;

    public string Begin(IReadOnlyList<SongLevelsInput> inputs)
    {
        var entry = new Entry(Guid.NewGuid().ToString("n"));

        // Started before it is published, so a fetch never finds it with nothing to wait on.
        entry.Read = Task.Run(() => ReadLevelsAsync(inputs, entry.Stopping), CancellationToken.None);

        lock (_gate)
        {
            _current?.Cancel();
            _current = entry;
        }

        return $"{options.CurrentValue.BaseAddress.TrimEnd('/')}{RoutePrefix}{entry.Token}";
    }

    public async Task<byte[]?> ReadAsync(string token, CancellationToken cancellationToken = default)
    {
        Entry? entry;
        lock (_gate) entry = _current?.Token == token ? _current : null;

        if (entry?.Read is not { } read) return null;

        return await read.WaitAsync(cancellationToken);
    }

    public void Clear()
    {
        lock (_gate)
        {
            _current?.Cancel();
            _current = null;
        }
    }

    public void Dispose() => Clear();

    private async Task<byte[]?> ReadLevelsAsync(IReadOnlyList<SongLevelsInput> inputs, CancellationToken cancellationToken)
    {
        var started = Stopwatch.StartNew();
        var start = new ProcessStartInfo(HlsMediaStreamService.ResolveFfmpegPath())
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in SongLevels.BuildArguments(inputs)) start.ArgumentList.Add(argument);

        try
        {
            using var process = Process.Start(start)
                ?? throw new InvalidOperationException("ffmpeg did not start.");

            // Behind the song's own encode and the screen for the CPU: the visualiser can wait.
            try { process.PriorityClass = ProcessPriorityClass.BelowNormal; }
            catch { /* not every platform lets a process lower another */ }

            await using var kill = cancellationToken.Register(() =>
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch { /* already gone */ }
            });

            var errors = process.StandardError.ReadToEndAsync(CancellationToken.None);
            var track = await SongLevels.AnalyseAsync(process.StandardOutput.BaseStream, cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                logger.LogWarning("Could not read the levels of {Input}: {Error}",
                    inputs[0].Input, (await errors).Trim());
                return null;
            }

            logger.LogInformation("Read the levels of {Input} in {Elapsed} ms: {Bytes} bytes",
                inputs[0].Input, started.ElapsedMilliseconds, track.Length);

            return track;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            // Decoration: the visualiser draws without the beat rather than the song failing.
            logger.LogWarning(ex, "Could not read the levels of {Input}", inputs[0].Input);
            return null;
        }
    }

    private sealed class Entry(string token)
    {
        private readonly CancellationTokenSource _stopping = new();

        public string Token { get; } = token;
        public CancellationToken Stopping => _stopping.Token;
        public Task<byte[]?>? Read { get; set; }

        public void Cancel()
        {
            try { _stopping.Cancel(); }
            catch (ObjectDisposedException) { /* already stopped */ }
        }
    }
}
