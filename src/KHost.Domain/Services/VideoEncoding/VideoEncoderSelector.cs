using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace KHost.Domain.Services.VideoEncoding;

/// <summary>Picks the H.264 encoder a stream is cut with.</summary>
public interface IVideoEncoderSelector
{
    /// <summary>The encoder to use under <paramref name="preference"/>; software whenever no
    /// hardware encoder works, never a failure.</summary>
    Task<VideoEncoderProfile> SelectAsync(VideoEncoderPreference preference, CancellationToken cancellationToken = default);

    /// <summary>An encoder that passed the probe then failed a real song; it is not chosen again
    /// for the life of the process.</summary>
    void ReportFailure(VideoEncoderProfile encoder);
}

/// <summary>Finds the first hardware encoder that really works here, once per ffmpeg the host runs.</summary>
/// <remarks>Listed is not working: a build lists every encoder it was compiled with, whatever the
/// machine has. Each candidate therefore runs a short real encode, cut into segments the way a song
/// is, and passes only when the keyframes land on the segment cadence.</remarks>
internal sealed class VideoEncoderSelector : BaseService, IVideoEncoderSelector
{
    private const string ProbePlaylist = "probe.m3u8";

    private static readonly TimeSpan ListTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Long enough for a driver's first session to open; a hang is a failure.</summary>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(20);

    private readonly IFfmpegProcessRunner _runner;
    private readonly IReadOnlyList<VideoEncoderProfile> _candidates;
    private readonly ConcurrentDictionary<string, Lazy<Task<VideoEncoderProfile?>>> _detected = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, bool> _failed = new(StringComparer.Ordinal);
    private int _warnedNoHardware;

    public VideoEncoderSelector(ILogger<VideoEncoderSelector> logger, IFfmpegProcessRunner runner)
        : this(logger, runner, CandidatesFor(CurrentPlatform()))
    {
    }

    internal VideoEncoderSelector(
        ILogger<VideoEncoderSelector> logger, IFfmpegProcessRunner runner, IReadOnlyList<VideoEncoderProfile> candidates)
        : base(logger)
    {
        _runner = runner;
        _candidates = candidates;
    }

    /// <summary>Hardware candidates in the order they are tried.</summary>
    /// <remarks>Windows puts Media Foundation last: it wraps whichever vendor encoder is present,
    /// with less control over rate and keyframes than that vendor's own. VA-API is left out on
    /// Linux, as it needs a device and an upload step in every filter graph.</remarks>
    internal static IReadOnlyList<VideoEncoderProfile> CandidatesFor(OSPlatform platform)
    {
        if (platform == OSPlatform.OSX) return [VideoEncoderProfile.VideoToolbox];

        if (platform == OSPlatform.Windows)
        {
            return
            [
                VideoEncoderProfile.QuickSync, VideoEncoderProfile.Amf, VideoEncoderProfile.Nvenc,
                VideoEncoderProfile.MediaFoundation,
            ];
        }

        if (platform == OSPlatform.Linux) return [VideoEncoderProfile.Nvenc, VideoEncoderProfile.QuickSync];

        return [];
    }

    public async Task<VideoEncoderProfile> SelectAsync(
        VideoEncoderPreference preference, CancellationToken cancellationToken = default)
    {
        if (preference == VideoEncoderPreference.Software) return VideoEncoderProfile.Software;

        // No ffmpeg is the encode's own failure to report, and a probe cached now would pin
        // libx264 past the install that fixes it. Keyed by path: a new copy may list other encoders.
        if (_runner.Locate() is not { } ffmpeg) return VideoEncoderProfile.Software;

        // Shared by every caller, so one caller giving up must not cancel the probe for the rest.
        var detected = await _detected
            .GetOrAdd(ffmpeg, path => new(() => DetectAsync(path), LazyThreadSafetyMode.ExecutionAndPublication))
            .Value.WaitAsync(cancellationToken);

        if (detected is not null && !_failed.ContainsKey(detected.Codec)) return detected;

        if (preference == VideoEncoderPreference.Hardware && Interlocked.Exchange(ref _warnedNoHardware, 1) == 0)
            Logger.LogWarning("Hardware video encoding was asked for, but no hardware encoder works here; using libx264");

        return VideoEncoderProfile.Software;
    }

    public void ReportFailure(VideoEncoderProfile encoder)
    {
        if (!encoder.IsHardware || !_failed.TryAdd(encoder.Codec, true)) return;

        Logger.LogWarning("Video encoder {Codec} failed a song; using libx264 until KHost restarts", encoder.Codec);
    }

    private async Task<VideoEncoderProfile?> DetectAsync(string ffmpeg)
    {
        if (_candidates.Count == 0)
        {
            Logger.LogInformation("Video encoding uses libx264: no hardware encoder is tried on this platform");
            return null;
        }

        var scratch = Path.Combine(Path.GetTempPath(), $"khost-encoder-probe-{Guid.NewGuid():n}");

        try
        {
            Directory.CreateDirectory(scratch);

            var listing = await _runner.RunAsync(ffmpeg, "-hide_banner -encoders", scratch, ListTimeout, CancellationToken.None);

            foreach (var candidate in _candidates)
            {
                if (!Lists(listing.Output, candidate.Codec))
                {
                    Logger.LogInformation("Video encoder {Codec}: not in this ffmpeg build", candidate.Codec);
                    continue;
                }

                if (await ProbeAsync(ffmpeg, candidate, scratch) is { } reason)
                {
                    Logger.LogInformation("Video encoder {Codec}: {Reason}", candidate.Codec, reason);
                    continue;
                }

                Logger.LogInformation("Video encoder {Codec}: works; video encoding uses it", candidate.Codec);
                return candidate;
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Could not probe for a hardware video encoder");
            return null;
        }
        finally
        {
            try { Directory.Delete(scratch, recursive: true); } catch { /* temp */ }
        }

        Logger.LogInformation("Video encoding uses libx264: no hardware encoder works here");
        return null;
    }

    /// <summary>Why <paramref name="candidate"/> fails, or null when it works.</summary>
    /// <remarks>Three seconds cut into one-second segments. The GOP ceiling is four seconds at this
    /// rate, so an encoder ignoring the forced keyframes yields one long segment.</remarks>
    private async Task<string?> ProbeAsync(string ffmpeg, VideoEncoderProfile candidate, string scratch)
    {
        foreach (var file in Directory.EnumerateFiles(scratch)) File.Delete(file);

        var run = await _runner.RunAsync(
            ffmpeg,
            "-hide_banner -loglevel error -f lavfi -i testsrc2=size=1280x720:rate=30 -t 3"
            + candidate.Arguments(720, 1)
            + " -f hls -hls_time 1 -hls_playlist_type event -hls_flags independent_segments"
            + $" -hls_segment_filename probe_%03d.ts {ProbePlaylist}",
            scratch, ProbeTimeout, CancellationToken.None);

        if (run.ExitCode != 0) return $"failed the test encode ({FirstLine(run.Error)})";

        var playlist = Path.Combine(scratch, ProbePlaylist);
        if (!File.Exists(playlist)) return "the test encode wrote no playlist";

        var durations = SegmentDurations(await File.ReadAllTextAsync(playlist));

        return durations.Count >= 2 && durations.SkipLast(1).All(d => d is >= 0.9 and <= 1.1)
            ? null
            : $"keyframes missed the segment cadence (segments {string.Join(", ", durations.Select(d => d.ToString("0.00", CultureInfo.InvariantCulture)))})";
    }

    /// <summary>Matches the name column of <c>ffmpeg -encoders</c>, not a substring of another.</summary>
    private static bool Lists(string listing, string codec) => listing
        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Any(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries) is [_, var name, ..] && name == codec);

    private static List<double> SegmentDurations(string playlist) =>
    [
        .. playlist.Split('\n')
            .Where(line => line.StartsWith("#EXTINF:", StringComparison.Ordinal))
            .Select(line => double.TryParse(
                line["#EXTINF:".Length..].TrimEnd(',', '\r'), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
                ? d
                : -1)
    ];

    private static string FirstLine(string text)
    {
        var line = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(l => l.Length > 0);

        return line ?? "no error text";
    }

    private static OSPlatform CurrentPlatform()
        => OperatingSystem.IsMacOS() ? OSPlatform.OSX
            : OperatingSystem.IsWindows() ? OSPlatform.Windows
            : OperatingSystem.IsLinux() ? OSPlatform.Linux
            : OSPlatform.FreeBSD;
}
