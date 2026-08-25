using KHost.Abstractions.Services;
using KHost.Plugins.Sdk.Messaging;
using KHost.Plugins.Sdk.Messaging.Messages;
using KHost.Plugins.Sdk.Models;
using KHost.Plugins.Sdk.Services;
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

    public BreakMusicState State { get; private set; } = BreakMusicState.Stopped;

    public BreakMusicTrack? CurrentTrack => _activeProvider?.CurrentTrack;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var venue = await _venues.ReadSelectedVenueAsync();

        _activeProvider = Resolve(venue?.Settings.BreakMusicProvider);

        Logger.LogInformation("Break music provider: {Provider}", _activeProvider?.SourceName ?? "none");

        _broker.Announce(new BreakMusicChanged());
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

    public async Task SuspendAsync(CancellationToken cancellationToken = default)
    {
        // Only playback is interrupted. Paused and Stopped are where a host put it, and coming
        // back on the song's behalf would override them.
        if (_activeProvider is not { } provider || State != BreakMusicState.Playing)
            return;

        await provider.StopAsync(SuspendFade, cancellationToken);

        State = BreakMusicState.Suspended;

        Logger.LogInformation("Break music suspended for something with its own audio");

        _broker.Announce(new BreakMusicChanged());
    }

    public async Task RestoreAsync(CancellationToken cancellationToken = default)
    {
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

    private void OnVenueChanged(SelectedVenueChanged message)
    {
        if (_activeProvider is { } provider)
            _ = ApplyVenueVolumeAsync(provider, CancellationToken.None);
    }

    private void OnProviderTrackChanged(BreakMusicTrackChanged message)
    {
        // Only the provider the venue chose speaks for the console; another one still winding
        // down would otherwise redraw the panel with its own track.
        if (message.ProviderSourceName != _activeProvider?.SourceName)
            return;

        _broker.Announce(new BreakMusicChanged());
    }
}
