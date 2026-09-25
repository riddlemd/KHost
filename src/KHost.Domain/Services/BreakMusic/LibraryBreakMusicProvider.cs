using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using Microsoft.Extensions.Logging;
using KHost.Domain.Services.Displays;
using KHost.Domain.Services.Displays.LocalScreen;
using System.Runtime.CompilerServices;

namespace KHost.Domain.Services.BreakMusic;

/// <summary>Break music from the host's library, sent to whatever the song is coming out of.</summary>
/// <remarks>It rides the second audio channel, which carries no timeline of its own.</remarks>
public class LibraryBreakMusicProvider : BaseService, IBreakMusicProvider, IDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly IMessageBroker _broker;
    private readonly IMediaPoolService _pools;
    private readonly IMediaService _media;
    private readonly IMediaStreamService _streams;
    private readonly IReadOnlyList<IDisplayProvider> _displays;

    // The one display that says when the bed ran out; the second channel has no timeline in the
    // contract, so this is the screens provider's own hook rather than anything a plugin raises.
    private readonly IReadOnlyList<LocalScreenDisplayProvider> _screens;
    private readonly IVenuesService _venues;

    private MediaStreamSession? _stream;
    private BreakMusicTrack? _currentTrack;

    private Task PublishTrackChangedAsync()
        => _broker.PublishAsync(new BreakMusicTrackChanged(SourceName));

    public LibraryBreakMusicProvider(
        ILogger<LibraryBreakMusicProvider> logger,
        IMediaPoolService pools,
        IMediaService media,
        IMediaStreamService streams,
        IEnumerable<IDisplayProvider> displays,
        IVenuesService venues,
        IMessageBroker broker)
        : base(logger)
    {
        _broker = broker;
        _pools = pools;
        _media = media;
        _streams = streams;
        _displays = [.. displays];
        _screens = [.. _displays.OfType<LocalScreenDisplayProvider>()];
        _venues = venues;

        foreach (var screens in _screens)
            screens.BackgroundTrackEnded += OnBackgroundTrackEnded;
    }


    public string DisplayName => "Library";
    public string SourceName => nameof(LibraryBreakMusicProvider);
    public bool RendersThroughHost => true;

    public BreakMusicTrack? CurrentTrack
    {
        get { lock (_lock) return _currentTrack; }
    }

    public async Task<bool> StartAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            return await PlayNextAsync(cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    public Task PauseAsync(CancellationToken cancellationToken = default)
        => SendToDisplaysAsync(display => display.PauseBackgroundAsync());

    public Task ResumeAsync(CancellationToken cancellationToken = default)
        => SendToDisplaysAsync(display => display.PlayBackgroundAsync());

    public async Task StopAsync(TimeSpan? fadeDuration = null, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            await SendToDisplaysAsync(display => display.StopBackgroundAsync(fadeDuration));

            _currentTrack = null;

            await CloseStreamAsync();
        }
        finally
        {
            _lock.Release();
        }

        await PublishTrackChangedAsync();
    }

    public async Task SkipAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            await PlayNextAsync(cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Deliberately nothing: this provider's audio rides the screen channel.</summary>
    /// <remarks>LocalScreenDisplayProvider sets that channel alongside the song's own.</remarks>
    public Task SetVolumeAsync(float volume, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public void Dispose()
    {
        foreach (var screens in _screens)
            screens.BackgroundTrackEnded -= OnBackgroundTrackEnded;

        _lock.Dispose();

        GC.SuppressFinalize(this);
    }

    /// <summary>The bed track played out, so the pool owes another one.</summary>
    private void OnBackgroundTrackEnded(object? sender, EventArgs e)
    {
        if (CurrentTrack is null) return;

        _ = AdvanceAfterEndAsync();
    }

    private async Task AdvanceAfterEndAsync()
    {
        await _lock.WaitAsync();
        try
        {
            await PlayNextAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to advance break music after a track ended");
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Caller holds the lock.</summary>
    private async Task<bool> PlayNextAsync(CancellationToken cancellationToken)
    {
        var venue = await _venues.ReadSelectedVenueAsync();

        if (venue?.Settings.BreakMusicPoolId is not { } poolId)
        {
            Logger.LogInformation("Break music not started: the venue has no pool chosen");
            return false;
        }

        var entry = await _pools.SelectNextAsync(poolId, venue.Id);

        // A bed track is the entry's own media; the ad composition's extra parts mean nothing here.
        if (entry?.MediaId is not { } mediaId)
        {
            Logger.LogInformation("Break music not started: pool {PoolId} holds nothing playable", poolId);
            return false;
        }

        var media = await _media.ReadAsync(mediaId);

        if (media is null || media.Status != MediaStatus.Ready)
        {
            Logger.LogWarning("Break music skipped media {MediaId}: missing or not Ready", mediaId);
            return false;
        }

        // Closed before the next one opens, not after: an orphaned transcode keeps burning CPU
        // for a track nobody is listening to.
        await CloseStreamAsync();

        _stream = await _streams.OpenAsync(media.FilePath, cancellationToken: cancellationToken);

        var track = new BackgroundLoad { StreamUrl = _stream.PlaylistUrl, AutoPlay = true };
        var sent = await SendToDisplaysAsync(display => display.LoadBackgroundAsync(track));

        if (!sent)
        {
            await CloseStreamAsync();
            return false;
        }

        _currentTrack = new BreakMusicTrack
        {
            Title = media.Title,
            Artist = media.Artist,
            Duration = media.Duration,
            MediaId = media.Id,
        };

        Logger.LogInformation("Break music playing '{Title}'", media.Title);

        await PublishTrackChangedAsync();

        return true;
    }

    private async Task CloseStreamAsync()
    {
        var stream = _stream;
        _stream = null;

        if (stream is null) return;

        try { await _streams.CloseAsync(stream.Id); }
        catch (Exception ex) { Logger.LogWarning(ex, "Failed to close break music stream {SessionId}", stream.Id); }
    }

    /// <summary>False when nothing is connected, so there is nowhere for the break music to play.</summary>
    /// <remarks>A display that cannot take a second channel inherits the no-op defaults and is
    /// still counted: the track is playing as far as the room is concerned, and a card naming it
    /// would otherwise be suppressed by a television that simply cannot carry the bed.</remarks>
    private async Task<bool> SendToDisplaysAsync(
        Func<IDisplayProvider, Task> call,
        [CallerArgumentExpression(nameof(call))] string what = "")
    {
        if (ConnectedDisplay.Find(_displays) is not { } connected)
        {
            Logger.LogInformation("Break music has nowhere to play: nothing is connected");
            return false;
        }

        try
        {
            await call(connected.Provider);
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to send {Call} to {Provider}", what, connected.Provider.Name);
            return false;
        }
    }
}
