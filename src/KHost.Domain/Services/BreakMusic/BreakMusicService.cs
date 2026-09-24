using KHost.Abstractions.Services;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using KHost.Abstractions.Models;
using Microsoft.Extensions.Logging;

namespace KHost.Domain.Services.BreakMusic;

public class BreakMusicService : BaseService, IBreakMusicService, IDisposable
{
    /// <summary>Long enough not to clip, short enough that the singer is not waiting on it.</summary>
    private static readonly TimeSpan SuspendFade = TimeSpan.FromSeconds(2);

    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly IMessageBroker _broker;
    private readonly SubscriptionSet _subscriptions = new();
    private readonly List<IBreakMusicProvider> _providers;
    private readonly IVenuesService _venues;

    private IBreakMusicProvider? _activeProvider;

    public BreakMusicService(
        ILogger<BreakMusicService> logger,
        IEnumerable<IBreakMusicProvider> providers,
        IVenuesService venues,
        IMessageBroker broker)
        : base(logger)
    {
        _broker = broker;
        _providers = [.. providers];
        _venues = venues;

        _subscriptions.Add(broker.Subscribe<BreakMusicTrackChanged>(OnProviderTrackChanged));

        // Only for a provider the host cannot reach: LocalScreenDisplayProvider already re-applies the
        // venue level to the screen when a venue is edited.
        _subscriptions.Add(broker.Subscribe<SelectedVenueChanged>(OnVenueChanged));
    }

    public IReadOnlyList<IBreakMusicProvider> Providers => _providers;
    public IBreakMusicProvider? ActiveProvider => _activeProvider;

    // Matched on source name, like every lookup here, not the concrete type: that is the key venues
    // already store (unrenameable without a migration), and resolvable without constructing the provider.
    public IBreakMusicProvider? LibraryProvider => _providers.FirstOrDefault(p =>
        string.Equals(p.SourceName, nameof(LibraryBreakMusicProvider), StringComparison.OrdinalIgnoreCase));

    /// <summary>True while a song or an ad holds the room, kept here not asked of playback.</summary>
    private bool _roomTaken;

    public BreakMusicState State { get; private set; } = BreakMusicState.Stopped;

    public BreakMusicTrack? CurrentTrack => _activeProvider?.CurrentTrack;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var venue = await _venues.ReadSelectedVenueAsync();

        _activeProvider = Resolve(venue?.Settings.BreakMusicProvider);

        Logger.LogInformation("Break music provider: {Provider}", _activeProvider?.SourceName ?? "none");

        await RunLockedAsync(() => AdoptProviderPlaybackAsync(cancellationToken), cancellationToken);

        _broker.Announce(new BreakMusicChanged());
    }

    /// <summary>Takes the provider's word for what is playing: it may outlive this process.</summary>
    /// <remarks>Starting at Stopped would lie while the room can still hear it.</remarks>
    private async Task AdoptProviderPlaybackAsync(CancellationToken cancellationToken)
    {
        if (_activeProvider is not { } provider)
            return;

        BreakMusicPlayback? playback;

        try
        {
            playback = await provider.ReadPlaybackAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // A provider that throws on a look must not stop the console coming up.
            Logger.LogWarning(ex, "Could not read what {Provider} is playing", provider.SourceName);
            return;
        }

        // Suspended is the host's own business and never comes back from a provider.
        var adopted = playback switch
        {
            BreakMusicPlayback.Playing => BreakMusicState.Playing,
            BreakMusicPlayback.Paused => BreakMusicState.Paused,
            BreakMusicPlayback.Stopped => BreakMusicState.Stopped,
            _ => State,
        };

        // Said once per change, not once per look. Startup and the watcher binding can both report
        // the same answer within a second, and a track-turnover look usually finds the transport unmoved.
        var changed = adopted != State;

        State = adopted;

        if (playback is not null && changed)
            Logger.LogInformation("{Provider} reports it is {Playback}", provider.SourceName, playback);
    }

    public async Task SetActiveProviderAsync(string sourceName, CancellationToken cancellationToken = default)
    {
        var next = Resolve(sourceName);

        if (next is null || ReferenceEquals(next, _activeProvider))
            return;

        // Stopped rather than handed over: the outgoing provider owns whatever it was playing, and
        // nothing else can stop another app once this one has let go of it.
        await StopAsync(cancellationToken);

        _activeProvider = next;

        _broker.Announce(new BreakMusicChanged());
    }

    public async Task<bool> StartAsync(CancellationToken cancellationToken = default)
    {
        if (_activeProvider is not { } provider)
            return false;

        if (RoomIsTaken(nameof(StartAsync)))
            return false;

        var started = await RunLockedAsync(async () =>
        {
            if (!await provider.StartAsync(cancellationToken))
                return false;

            await ApplyVenueVolumeAsync(provider, cancellationToken);

            State = BreakMusicState.Playing;
            return true;
        }, cancellationToken);

        if (!started)
            return false;

        _broker.Announce(new BreakMusicChanged());
        return true;
    }

    public async Task PauseAsync(CancellationToken cancellationToken = default)
    {
        if (_activeProvider is not { } provider || State != BreakMusicState.Playing)
            return;

        await RunLockedAsync(async () =>
        {
            await provider.PauseAsync(cancellationToken);
            State = BreakMusicState.Paused;
        }, cancellationToken);

        _broker.Announce(new BreakMusicChanged());
    }

    public async Task ResumeAsync(CancellationToken cancellationToken = default)
    {
        if (_activeProvider is not { } provider || State != BreakMusicState.Paused)
            return;

        if (RoomIsTaken(nameof(ResumeAsync)))
            return;

        await RunLockedAsync(async () =>
        {
            await provider.ResumeAsync(cancellationToken);
            State = BreakMusicState.Playing;
        }, cancellationToken);

        _broker.Announce(new BreakMusicChanged());
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_activeProvider is not { } provider)
            return;

        await RunLockedAsync(async () =>
        {
            await provider.StopAsync(cancellationToken: cancellationToken);
            State = BreakMusicState.Stopped;
        }, cancellationToken);

        _broker.Announce(new BreakMusicChanged());
    }

    public async Task SkipAsync(CancellationToken cancellationToken = default)
    {
        if (_activeProvider is not { } provider || State == BreakMusicState.Stopped)
            return;

        // Skipping starts audio too, as the note below says, so it is refused for the same reason.
        if (RoomIsTaken(nameof(SkipAsync)))
            return;

        await RunLockedAsync(async () =>
        {
            await provider.SkipAsync(cancellationToken);

            // Skipping is a request to hear the next track, and every provider starts it. Left on
            // Paused, the bar would offer play while the room hears music; Suspended returns on its
            // own, not promoted.
            if (State == BreakMusicState.Paused)
                State = BreakMusicState.Playing;
        }, cancellationToken);

        _broker.Announce(new BreakMusicChanged());
    }

    /// <summary>Pushes the venue's level at a provider the host cannot reach directly.</summary>
    /// <remarks>One rendering through the host is set by LocalScreenDisplayProvider instead.</remarks>
    private async Task ApplyVenueVolumeAsync(IBreakMusicProvider provider, CancellationToken cancellationToken)
    {
        if (provider.RendersThroughHost)
            return;

        try
        {
            var venue = await _venues.ReadSelectedVenueAsync();
            var volume = VenueVolume.ToGain(venue?.Settings.DefaultVolume ?? 100);

            await provider.SetVolumeAsync(volume, cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Could not apply the venue volume to {Provider}", provider.SourceName);
        }
    }

    /// <summary>Every state transition serializes through here, so two calls in flight cannot each
    /// read a state the other is about to overwrite. The caller announces once outside it — never
    /// under this lock, the same rule a broker publish follows.</summary>
    private async Task<T> RunLockedAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            return await action();
        }
        finally
        {
            _lock.Release();
        }
    }

    private Task RunLockedAsync(Func<Task> action, CancellationToken cancellationToken)
        => RunLockedAsync(async () => { await action(); return true; }, cancellationToken);

    /// <summary>Every way the bed can reach the room goes through this: start, resume, skip.</summary>
    private bool RoomIsTaken(string action)
    {
        if (!_roomTaken)
            return false;

        Logger.LogInformation("Break music {Action} refused: something with its own audio has the room", action);

        return true;
    }

    public async Task SuspendAsync(CancellationToken cancellationToken = default)
    {
        // Before the provider check: whether anything is playing has no bearing on whether a
        // singer now has the room, and StartAsync has to refuse either way.
        _roomTaken = true;

        if (_activeProvider is not { } provider)
            return;

        var suspended = await RunLockedAsync(async () =>
        {
            // Asked before deciding: a provider driving another app can start, stop or pause
            // without this service hearing about it. Only what it cannot tell falls back to the
            // state kept here.
            await AdoptProviderPlaybackAsync(cancellationToken);

            // Only playback is interrupted. Paused and Stopped are where a host put it, and coming
            // back on the song's behalf would override them.
            if (State != BreakMusicState.Playing)
                return false;

            await provider.StopAsync(SuspendFade, cancellationToken);

            State = BreakMusicState.Suspended;
            return true;
        }, cancellationToken);

        if (!suspended)
            return;

        Logger.LogInformation("Break music suspended for something with its own audio");

        _broker.Announce(new BreakMusicChanged());
    }

    public async Task RestoreAsync(CancellationToken cancellationToken = default)
    {
        // Cleared first, and above the state check: the room is free again whether or not there
        // was a bed to bring back, and StartAsync below would otherwise refuse its own restore.
        _roomTaken = false;

        // Suspended is only ever reached from Playing, so this one check carries the whole rule:
        // a bed the host paused or stopped never entered this state and is left where they put it.
        if (State != BreakMusicState.Suspended)
            return;

        // Started rather than resumed: the suspend stopped the provider outright, because a bed
        // held open across a whole song is a transcode running for nobody. Awaited to completion
        // (and its own lock released) before this locks again, or the two would deadlock each other.
        if (await StartAsync(cancellationToken))
            return;

        await RunLockedAsync(() =>
        {
            State = BreakMusicState.Stopped;
            return Task.CompletedTask;
        }, cancellationToken);

        _broker.Announce(new BreakMusicChanged());
    }

    public void Dispose()
    {
        _subscriptions.Dispose();

        _lock.Dispose();

        GC.SuppressFinalize(this);
    }

    private IBreakMusicProvider? Resolve(string? sourceName)
    {
        if (!string.IsNullOrWhiteSpace(sourceName))
        {
            var named = _providers.FirstOrDefault(p =>
                string.Equals(p.SourceName, sourceName, StringComparison.OrdinalIgnoreCase));

            if (named is not null)
                return named;

            Logger.LogWarning("Break music provider '{Source}' is not loaded; falling back", sourceName);
        }

        // Named rather than "whichever registered first": plugins register after the domain
        // today, but that ordering is not something a venue's default should rest on.
        return LibraryProvider ?? _providers.FirstOrDefault();
    }

    // The mode is part of the venue's audio baseline like its volume: this message means the console is
    // running a different venue (or the current one was edited), and its named mode should play.
    private void OnVenueChanged(SelectedVenueChanged message)
        => _ = ReapplyVenueAsync(CancellationToken.None);

    private async Task ReapplyVenueAsync(CancellationToken cancellationToken)
    {
        var venue = await _venues.ReadSelectedVenueAsync();

        if (venue?.Settings.BreakMusicProvider is { } source && !string.IsNullOrWhiteSpace(source))
            await SetActiveProviderAsync(source, cancellationToken);

        if (_activeProvider is { } provider)
            await ApplyVenueVolumeAsync(provider, cancellationToken);
    }

    private void OnProviderTrackChanged(BreakMusicTrackChanged message)
    {
        // Only the provider the venue chose speaks for the console; another one still winding
        // down would otherwise redraw the panel with its own track.
        if (message.ProviderSourceName != _activeProvider?.SourceName)
            return;

        // Off the broker's chain, not awaited: handlers run one at a time, and ScreenConnected arrives on
        // the SignalR hub thread already holding a lock. Asking another app here could stall registration.
        _ = Task.Run(async () =>
        {
            // The transport may have moved as well as the track: a host pausing in the other app
            // looks like this. Suspended is left alone, since the song that suspended it is still playing.
            if (State != BreakMusicState.Suspended)
                await RunLockedAsync(() => AdoptProviderPlaybackAsync(CancellationToken.None), CancellationToken.None);

            _broker.Announce(new BreakMusicChanged());
        });
    }
}
