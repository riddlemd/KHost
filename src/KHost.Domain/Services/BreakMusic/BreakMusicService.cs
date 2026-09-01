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

        // Only for a provider the host cannot reach: ScreenCoordination already re-applies the
        // venue level to every screen when a venue is edited.
        _subscriptions.Add(broker.Subscribe<SelectedVenueChanged>(OnVenueChanged));
    }

    public IReadOnlyList<IBreakMusicProvider> Providers => _providers;
    public IBreakMusicProvider? ActiveProvider => _activeProvider;

    // Matched on the source name like every other lookup here, rather than on the concrete type:
    // that name is the key venues already store, so it cannot be renamed without a migration
    // anyway, and matching it keeps this resolvable without constructing the real provider.
    public IBreakMusicProvider? LibraryProvider => _providers.FirstOrDefault(p =>
        string.Equals(p.SourceName, nameof(LibraryBreakMusicProvider), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// True while a song or an audible ad holds the room. Kept here rather than asked of playback,
    /// which already depends on this service — the two calls playback makes on the way in and out
    /// are the same two that decide it.
    /// </summary>
    private bool _roomTaken;

    public BreakMusicState State { get; private set; } = BreakMusicState.Stopped;

    public BreakMusicTrack? CurrentTrack => _activeProvider?.CurrentTrack;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var venue = await _venues.ReadSelectedVenueAsync();

        _activeProvider = Resolve(venue?.Settings.BreakMusicProvider);

        Logger.LogInformation("Break music provider: {Provider}", _activeProvider?.SourceName ?? "none");

        await AdoptProviderPlaybackAsync(cancellationToken);

        _broker.Announce(new BreakMusicChanged());
    }

    /// <summary>
    /// Takes the provider's word for what is playing. One driving another app outlives this
    /// process, so starting at Stopped would leave the console saying the bed is off while the
    /// room hears it — and, worse, leave <see cref="SuspendAsync"/> with no reason to clear the
    /// air for a singer. A provider that cannot tell leaves the state where it was.
    /// </summary>
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

        // Said once per change, not once per look. Startup asks and the watcher binding says
        // Spotify is there, so the same answer arrives twice within a second — and a provider
        // announcing a track turnover is a look whose transport usually has not moved at all.
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

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (!await provider.StartAsync(cancellationToken))
                return false;

            await ApplyVenueVolumeAsync(provider, cancellationToken);

            State = BreakMusicState.Playing;
        }
        finally
        {
            _lock.Release();
        }

        _broker.Announce(new BreakMusicChanged());
        return true;
    }

    public async Task PauseAsync(CancellationToken cancellationToken = default)
    {
        if (_activeProvider is not { } provider || State != BreakMusicState.Playing)
            return;

        await provider.PauseAsync(cancellationToken);

        State = BreakMusicState.Paused;

        _broker.Announce(new BreakMusicChanged());
    }

    public async Task ResumeAsync(CancellationToken cancellationToken = default)
    {
        if (_activeProvider is not { } provider || State != BreakMusicState.Paused)
            return;

        if (RoomIsTaken(nameof(ResumeAsync)))
            return;

        await provider.ResumeAsync(cancellationToken);

        State = BreakMusicState.Playing;

        _broker.Announce(new BreakMusicChanged());
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_activeProvider is not { } provider)
            return;

        await provider.StopAsync(cancellationToken: cancellationToken);

        State = BreakMusicState.Stopped;

        _broker.Announce(new BreakMusicChanged());
    }

    public async Task SkipAsync(CancellationToken cancellationToken = default)
    {
        if (_activeProvider is not { } provider || State == BreakMusicState.Stopped)
            return;

        // Skipping starts audio too, as the note below says, so it is refused for the same reason.
        if (RoomIsTaken(nameof(SkipAsync)))
            return;

        await provider.SkipAsync(cancellationToken);

        // Skipping is a request to hear the next track, and every provider starts it: the library
        // one plays what it loads, and a media-key next resumes a paused Spotify. Left on Paused,
        // the bar went on offering play while the room could already hear the music. Suspended is
        // not promoted — that one is yielding to a singer, and it comes back on its own.
        if (State == BreakMusicState.Paused)
            State = BreakMusicState.Playing;

        _broker.Announce(new BreakMusicChanged());
    }

    /// <summary>
    /// Pushes the venue's level at a provider the host cannot reach. One that renders through the
    /// host needs nothing here: its channel is set by ScreenCoordination alongside the song's, so
    /// doing it again would be a second place for the same number to drift.
    /// </summary>
    private async Task ApplyVenueVolumeAsync(IBreakMusicProvider provider, CancellationToken cancellationToken)
    {
        if (provider.RendersThroughHost)
            return;

        try
        {
            var venue = await _venues.ReadSelectedVenueAsync();
            var volume = Math.Clamp((venue?.Settings.DefaultVolume ?? 100) / 100f, 0f, 1f);

            await provider.SetVolumeAsync(volume, cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Could not apply the venue volume to {Provider}", provider.SourceName);
        }
    }

    /// <summary>
    /// Every way the bed can reach the room goes through this. Start is not the only one: a paused
    /// bed resumes, and a skip starts the next track on every provider — and a song loading over a
    /// *paused* bed leaves it paused rather than suspended, so both were reachable mid-song.
    /// </summary>
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

        // Asked before deciding, because a provider driving another app can have started, stopped
        // or been paused without this service hearing about it. Only what it cannot tell falls
        // back to the state kept here.
        await AdoptProviderPlaybackAsync(cancellationToken);

        // Only playback is interrupted. Paused and Stopped are where a host put it, and coming
        // back on the song's behalf would override them.
        if (State != BreakMusicState.Playing)
            return;

        await provider.StopAsync(SuspendFade, cancellationToken);

        State = BreakMusicState.Suspended;

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
        // held open across a whole song is a transcode running for nobody.
        if (!await StartAsync(cancellationToken))
        {
            State = BreakMusicState.Stopped;
            _broker.Announce(new BreakMusicChanged());
        }
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
        return _providers.FirstOrDefault(p => p is LibraryBreakMusicProvider)
            ?? _providers.FirstOrDefault();
    }

    // The mode is part of the venue's audio baseline just as its volume is: this message means the
    // console is running a different venue, or the one it is running was edited, and either way the
    // mode that venue names is the one that should be playing. Before, only the page that owned the
    // selector applied it, so a mode changed anywhere else was not picked up until a restart.
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

        // Off the broker's chain, not awaited on it. Handlers run one at a time, and
        // ScreenConnected arrives on the SignalR hub thread already holding a lock — asking
        // another app what it is playing from here stalls that thread long enough to lose a
        // screen that was in the middle of registering.
        _ = Task.Run(async () =>
        {
            // The transport may have moved as well as the track — this is what a host pressing
            // pause in the other app's own window looks like from here. Suspended is left alone:
            // the song that suspended it is still playing, and the provider does not end that.
            if (State != BreakMusicState.Suspended)
                await AdoptProviderPlaybackAsync(CancellationToken.None);

            _broker.Announce(new BreakMusicChanged());
        });
    }
}
