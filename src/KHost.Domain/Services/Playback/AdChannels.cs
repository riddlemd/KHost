using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace KHost.Domain.Services.Playback;

/// <summary>The ad holding the main channel, and its own audio borrowing the background channel
/// from break music.</summary>
/// <remarks>Owned by <see cref="PlaybackService"/>, which loads, plays and ends an ad as it does a
/// song; this holds what an ad adds on top and gives the background channel back.</remarks>
internal sealed class AdChannels
{
    private readonly IMediaStreamService _mediaStreams;
    private readonly DisplayLink _link;
    private readonly ILogger _logger;
    private readonly Func<MediaStreamSession?, Task> _closeSession;

    // Unguarded, like the transport state it rides with.
    // An ad's own audio track, which borrows the background channel from break music.
    private MediaStreamSession? _audioStream;

    public AdChannels(
        IMediaStreamService mediaStreams,
        DisplayLink link,
        ILogger logger,
        Func<MediaStreamSession?, Task> closeSession)
    {
        _mediaStreams = mediaStreams;
        _link = link;
        _logger = logger;
        _closeSession = closeSession;
    }

    /// <summary>The ad on the main channel. Its Duration, not any one file's, is what the clock runs
    /// out.</summary>
    /// <remarks>Unguarded, like the transport state it rides with.</remarks>
    public AdPlayback? Current { get; set; }

    public bool IsPlaying => Current is not null;

    /// <summary>Whether <paramref name="ad"/> can go out now, logging why not.</summary>
    /// <param name="performanceLoaded">Never over a singer: an ad waits for the gap rather than
    /// cutting a performance short.</param>
    public bool Accepts(AdPlayback ad, bool performanceLoaded)
    {
        if (ad.IsEmpty)
        {
            _logger.LogWarning("Ad refused: nothing to show and nothing to play");
            return false;
        }

        // Nothing here is watching an ad the way a host watches a song, and the clock ends one by
        // its duration: without one this would hold the main channel all night.
        if (ad.Duration <= TimeSpan.Zero)
        {
            _logger.LogWarning("Ad refused: no duration");
            return false;
        }

        if (ad.Visual is { Status: not MediaStatus.Ready } visualStatus)
        {
            _logger.LogWarning("Ad refused: visual {MediaId} is {Status}, not Ready", visualStatus.Id, visualStatus.Status);
            return false;
        }

        if (ad.Audio is { Status: not MediaStatus.Ready } audioStatus)
        {
            _logger.LogWarning("Ad refused: audio {MediaId} is {Status}, not Ready", audioStatus.Id, audioStatus.Status);
            return false;
        }

        if (performanceLoaded)
        {
            _logger.LogWarning("Ad refused: a performance is loaded");
            return false;
        }

        return true;
    }

    /// <summary>Starts the ad's own audio on the background channel.</summary>
    /// <remarks>Opened at the offset rather than trimmed: a clip out of a longer file costs no
    /// re-encode, and the host clock stops it at the ad's duration.</remarks>
    public async Task PlayAudioAsync(Media audio, TimeSpan start)
    {
        _audioStream = await _mediaStreams.OpenAsync(audio.FilePath, start);

        var bed = new BackgroundLoad { StreamUrl = _audioStream.PlaylistUrl!, AutoPlay = true };
        await _link.SendAsync(display => display.LoadBackgroundAsync(bed));
    }

    /// <summary>Ends an ad before its clock ran out.</summary>
    /// <remarks>None of what follows a finished one (the bed, the venue card) happens here.</remarks>
    public async Task CancelAsync()
    {
        _logger.LogInformation("Ad cut short");

        Current = null;

        await ReleaseAudioAsync();
    }

    /// <summary>Gives the background channel back, so the bed or a song can take it.</summary>
    public async Task ReleaseAudioAsync()
    {
        if (_audioStream is null) return;

        await _link.SendAsync(display => display.StopBackgroundAsync());

        var stream = _audioStream;
        _audioStream = null;

        await _closeSession(stream);
    }
}
