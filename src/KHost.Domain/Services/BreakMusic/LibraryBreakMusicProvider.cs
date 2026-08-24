using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Abstractions.Services.IPC;
using KHost.Plugins.Sdk.Messaging;
using KHost.Plugins.Sdk.Messaging.Messages;
using KHost.Plugins.Sdk.Models;
using KHost.Plugins.Sdk.Services;
using Microsoft.Extensions.Logging;

namespace KHost.Domain.Services.BreakMusic;

/// <summary>
/// Break music out of the host's own library, drawn from the venue's pool and sent to the screen
/// the room hears. Only that screen: the bed carries no timeline, so there is no group for a
/// second screen to be in step with, and two screens playing it would be two beds in one room.
/// </summary>
public class LibraryBreakMusicProvider : BaseService, IBreakMusicProvider, IDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly IMediaPoolService _pools;
    private readonly IMediaService _media;
    private readonly IMediaStreamService _streams;
    private readonly IScreenServer _screenServer;
    private readonly IScreenCoordinationService _screenCoordination;
    private readonly IVenuesService _venues;

    private MediaStreamSession? _stream;
    private BreakMusicTrack? _currentTrack;

    private Task PublishTrackChangedAsync()
        => Broker?.PublishAsync(new BreakMusicTrackChanged(SourceName)) ?? Task.CompletedTask;

    public LibraryBreakMusicProvider(
        ILogger<LibraryBreakMusicProvider> logger,
        IMediaPoolService pools,
        IMediaService media,
        IMediaStreamService streams,
        IScreenServer screenServer,
        IScreenCoordinationService screenCoordination,
        IVenuesService venues,
        IMessageBroker broker)
        : base(logger, broker)
    {
        _pools = pools;
        _media = media;
        _streams = streams;
        _screenServer = screenServer;
        _screenCoordination = screenCoordination;
        _venues = venues;

        _screenServer.StateReceived += OnScreenStateReceived;
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
        => SendToAudioScreenAsync(new PauseBackgroundCommand());

    public Task ResumeAsync(CancellationToken cancellationToken = default)
        => SendToAudioScreenAsync(new PlayBackgroundCommand());

    public async Task StopAsync(TimeSpan? fadeDuration = null, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            await SendToAudioScreenAsync(new StopBackgroundCommand { FadeDuration = fadeDuration });

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

    /// <summary>
    /// Deliberately nothing. This provider's audio rides the screen, and ScreenCoordination sets
    /// that channel from the venue alongside the song's — one venue level, set in one place.
    /// </summary>
    public Task SetVolumeAsync(float volume, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public void Dispose()
    {
        _screenServer.StateReceived -= OnScreenStateReceived;
        _lock.Dispose();

        GC.SuppressFinalize(this);
    }

    /// <summary>The bed track played out, so the pool owes another one.</summary>
    private void OnScreenStateReceived(object? sender, ScreenStateReceivedEventArgs e)
    {
        if (e.State is not ScreenBackgroundState { HasEnded: true }) return;
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

        var sent = await SendToAudioScreenAsync(new LoadBackgroundCommand
        {
            StreamUrl = _stream.PlaylistUrl,
            AutoPlay = true,
        });

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

    /// <summary>False when no screen is carrying the room's audio, so there is nowhere to play.</summary>
    private async Task<bool> SendToAudioScreenAsync(IScreenCommand command)
    {
        try
        {
            var screenId = await _screenCoordination.EnsureRolesAsync();

            if (screenId is null)
            {
                Logger.LogInformation("Break music has nowhere to play: no screen carries the room's audio");
                return false;
            }

            await _screenServer.SendCommandAsync(screenId, command);
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to send {Command} to the audio screen", command.GetType().Name);
            return false;
        }
    }
}
