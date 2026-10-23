using KHost.Abstractions.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KHost.Domain.Services.Displays.LocalScreen;

/// <summary>Turns a library video into something the local screen can play, muted and looping,
/// under a song's words.</summary>
/// <remarks>Host plumbing for the local screen's visualiser, not a contract: it hands out URLs
/// serving files off this machine, so no plugin may reach it.</remarks>
public interface IVideoBackdropService
{
    /// <summary>A URL the screen can play the video from; null when it cannot be had (the file is
    /// gone, it is not a video, or it could not be read or encoded).</summary>
    /// <remarks>A video the screen cannot play as it is is encoded first, and this waits for it.
    /// Throws only when cancelled.</remarks>
    Task<string?> UrlForAsync(Media media, CancellationToken cancellationToken = default);

    /// <summary>As <see cref="UrlForAsync"/>, but only for a video the screen plays as it is; null
    /// for one that would need encoding. Never encodes.</summary>
    Task<string?> DirectUrlForAsync(Media media, CancellationToken cancellationToken = default);

    /// <summary>The file behind a token this service handed out; null for any other token, or for a
    /// file since gone.</summary>
    string? ResolveFile(string token);
}

/// <inheritdoc />
public sealed class VideoBackdropService : BaseService, IVideoBackdropService, IDisposable
{
    /// <summary>Under the host's media surface, beside <c>/media/levels</c>.</summary>
    public const string RoutePrefix = "/media/backdrops/";

    private readonly IOptionsMonitor<HlsMediaStreamService.ServiceOptions> _options;
    private readonly IVideoBackdropProbe _probe;
    private readonly IVideoBackdropEncoder _encoder;
    private readonly IDisplayCodecs? _displayCodecs;
    private readonly string _root;
    private readonly string _directory;
    private readonly CancellationTokenSource _stopping = new();

    private readonly Lock _gate = new();
    private readonly Dictionary<string, string> _files = [];
    private readonly Dictionary<string, string> _tokens = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Task<string?>> _encodes = new(StringComparer.Ordinal);

    public VideoBackdropService(
        ILogger<VideoBackdropService> logger,
        IOptionsMonitor<HlsMediaStreamService.ServiceOptions> options,
        IVideoBackdropProbe probe,
        IVideoBackdropEncoder encoder,
        IDisplayCodecs? displayCodecs = null)
        : this(logger, options, probe, encoder, Path.Combine(Path.GetTempPath(), "khost-backdrops"), displayCodecs)
    {
    }

    internal VideoBackdropService(
        ILogger<VideoBackdropService> logger,
        IOptionsMonitor<HlsMediaStreamService.ServiceOptions> options,
        IVideoBackdropProbe probe,
        IVideoBackdropEncoder encoder,
        string root,
        IDisplayCodecs? displayCodecs = null)
        : base(logger)
    {
        _displayCodecs = displayCodecs;
        _options = options;
        _probe = probe;
        _encoder = encoder;
        _root = root;

        // Before this run makes a folder of its own, so only another process's are judged.
        var swept = HlsSessionSweeper.Sweep(_root, DateTime.UtcNow, HlsSessionSweeper.IsRunning);
        if (swept.Count > 0)
            Logger.LogInformation("Swept {Count} orphaned video backdrop folder(s) from {Root}: {Folders}",
                swept.Count, _root, string.Join(", ", swept));

        _directory = Path.Combine(_root, Guid.NewGuid().ToString("n"));
    }

    /// <summary>The folder this run's encodes are written to, made on first use.</summary>
    internal string WorkingDirectory => _directory;

    public Task<string?> UrlForAsync(Media media, CancellationToken cancellationToken = default)
        => ResolveAsync(media, mayEncode: true, cancellationToken);

    public Task<string?> DirectUrlForAsync(Media media, CancellationToken cancellationToken = default)
        => ResolveAsync(media, mayEncode: false, cancellationToken);

    public string? ResolveFile(string token)
    {
        string? path;
        lock (_gate) path = _files.GetValueOrDefault(token);

        return path is not null && File.Exists(path) ? path : null;
    }

    public void Dispose()
    {
        _stopping.Cancel();

        try
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
        // An encode still letting go of its output; the next start sweeps the folder.
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>Whether the screen's video element plays the file as it is: 8-bit 4:2:0 H.264 in an
    /// MP4 or QuickTime container, with no sound or AAC. A screen with no H.264
    /// (<paramref name="openCodecs"/>) takes 8-bit 4:2:0 VP9 or AV1 in MP4 or WebM, with no sound,
    /// Opus or Vorbis.</summary>
    internal static bool PlaysAsIs(VideoBackdropFacts facts, bool openCodecs = false)
        => (openCodecs
                ? Is(facts.VideoCodec, "vp9", "av1")
                  && (InContainer(facts, "mp4") || InContainer(facts, "webm") || InContainer(facts, "matroska"))
                  && (facts.AudioCodec is null || Is(facts.AudioCodec, "opus", "vorbis"))
                : Is(facts.VideoCodec, "h264")
                  && (InContainer(facts, "mp4") || InContainer(facts, "mov"))
                  && (facts.AudioCodec is null || Is(facts.AudioCodec, "aac")))
            // A web view plays only 8-bit 4:2:0 reliably; 10-bit or 4:2:2 decodes black or not at all.
            && Is(facts.PixelFormat, "yuv420p", "yuvj420p");

    private static bool Is(string? value, params string[] any)
        => any.Any(name => string.Equals(value, name, StringComparison.OrdinalIgnoreCase));

    private static bool InContainer(VideoBackdropFacts facts, string name)
        => facts.FormatName.Contains(name, StringComparison.OrdinalIgnoreCase);

    private async Task<string?> ResolveAsync(Media media, bool mayEncode, CancellationToken cancellationToken)
    {
        if (media.Type != MediaType.Video)
        {
            Logger.LogWarning("'{Title}' is not a video, so it cannot be drawn under the words", media.Title);
            return null;
        }

        var path = media.FilePath;
        if (!File.Exists(path))
        {
            Logger.LogWarning("The video '{Title}' is not at '{FilePath}' any more", media.Title, path);
            return null;
        }

        // Asked every time: a file swapped under an unchanged path must be read again.
        if (await _probe.ProbeAsync(path, cancellationToken) is not { } facts) return null;

        if (facts.VideoCodec is null)
        {
            Logger.LogWarning("'{FilePath}' has no picture to draw under the words", path);
            return null;
        }

        // Read per call, so a screen with no H.264 gets its own encode of the same file.
        var openCodecs = _displayCodecs?.OpenCodecsOnly == true;
        if (PlaysAsIs(facts, openCodecs)) return UrlFor(path);
        if (!mayEncode) return null;

        return await EncodedAsync(path, openCodecs, cancellationToken) is { } encoded ? UrlFor(encoded) : null;
    }

    /// <summary>The run's encode of <paramref name="source"/> as it is on disk now, started if there
    /// is none; every asker of the same file shares one.</summary>
    private async Task<string?> EncodedAsync(string source, bool openCodecs, CancellationToken cancellationToken)
    {
        string key;
        try { key = $"{Path.GetFullPath(source)}|{File.GetLastWriteTimeUtc(source).Ticks}|{(openCodecs ? "vp9" : "h264")}"; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.LogWarning(ex, "Could not read '{FilePath}' to encode it", source);
            return null;
        }

        Task<string?> encode;
        lock (_gate)
        {
            if (!_encodes.TryGetValue(key, out var running))
            {
                running = Task.Run(() => EncodeAsync(source, key, openCodecs), CancellationToken.None);
                _encodes[key] = running;
            }

            encode = running;
        }

        // Awaited, not joined: a song that ends leaves the encode running for the next to use.
        return await encode.WaitAsync(cancellationToken);
    }

    private async Task<string?> EncodeAsync(string source, string key, bool openCodecs)
    {
        var name = Guid.NewGuid().ToString("n");
        var output = Path.Combine(_directory, name + ".mp4");
        var partial = output + ".part";

        try
        {
            if (!Directory.Exists(_directory))
            {
                Directory.CreateDirectory(_directory);
                HlsSessionSweeper.WriteOwner(_directory);
            }

            Logger.LogInformation("Encoding '{FilePath}' for the screen to play under the words", source);

            // Written aside and moved when whole, so the screen is never served half a file.
            if (await _encoder.EncodeAsync(source, partial, openCodecs, _stopping.Token) && File.Exists(partial))
            {
                File.Move(partial, output);
                Logger.LogInformation("Encoded '{FilePath}' for the screen", source);
                return output;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Could not encode '{FilePath}' for the screen", source);
        }

        try { File.Delete(partial); }
        catch { /* never written, or already gone with the folder */ }

        // A failure is not kept: the next ask tries again, by when ffmpeg may be there.
        lock (_gate) _encodes.Remove(key);

        return null;
    }

    private string UrlFor(string path)
    {
        string token;
        lock (_gate)
        {
            if (!_tokens.TryGetValue(path, out var known))
            {
                known = Guid.NewGuid().ToString("n");
                _tokens[path] = known;
                _files[known] = path;
            }

            token = known;
        }

        return $"{_options.CurrentValue.BaseAddress.TrimEnd('/')}{RoutePrefix}{token}";
    }
}
