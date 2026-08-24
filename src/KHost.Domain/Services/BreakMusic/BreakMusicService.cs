using KHost.Abstractions.Services;
using KHost.Plugins.Sdk.Models;
using KHost.Plugins.Sdk.Services;
using Microsoft.Extensions.Logging;

namespace KHost.Domain.Services.BreakMusic;

public class BreakMusicService : BaseService, IBreakMusicService, IDisposable
{
    /// <summary>Long enough not to clip, short enough that the singer is not waiting on it.</summary>
    private static readonly TimeSpan SuspendFade = TimeSpan.FromSeconds(2);

    private const float DefaultVolume = 0.6f;

    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly List<IBreakMusicProvider> _providers;
    private readonly IVenuesService _venues;

    private IBreakMusicProvider? _activeProvider;

    /// <summary>
    /// Whether the bed was playing when something with its own audio took over. A host who paused
    /// it meant to, so only a suspend that interrupted playback is undone.
    /// </summary>
    private bool _suspendedFromPlaying;

    public BreakMusicService(
        ILogger<BreakMusicService> logger,
        IEnumerable<IBreakMusicProvider> providers,
        IVenuesService venues)
        : base(logger)
    {
        _providers = [.. providers];
        _venues = venues;

        foreach (var provider in _providers)
            provider.TrackChanged += OnProviderTrackChanged;
    }

    public IReadOnlyList<IBreakMusicProvider> Providers => _providers;
    public IBreakMusicProvider? ActiveProvider => _activeProvider;

    public BreakMusicState State { get; private set; } = BreakMusicState.Stopped;

    public BreakMusicTrack? CurrentTrack => _activeProvider?.CurrentTrack;

    public float Volume { get; private set; } = DefaultVolume;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var venue = await _venues.ReadSelectedVenueAsync();

        _activeProvider = Resolve(venue?.Settings.BreakMusicProvider);

        if (venue?.Settings.BreakMusicVolume is { } stored)
            Volume = Math.Clamp(stored / 100f, 0f, 1f);

        Logger.LogInformation("Break music provider: {Provider}", _activeProvider?.SourceName ?? "none");

        InvokeStateChanged();
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

        InvokeStateChanged();
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

            await provider.SetVolumeAsync(Volume, cancellationToken);

            State = BreakMusicState.Playing;
            _suspendedFromPlaying = false;
        }
        finally
        {
            _lock.Release();
        }

        InvokeStateChanged();
        return true;
    }

    public async Task PauseAsync(CancellationToken cancellationToken = default)
    {
        if (_activeProvider is not { } provider || State != BreakMusicState.Playing)
            return;

        await provider.PauseAsync(cancellationToken);

        State = BreakMusicState.Paused;

        InvokeStateChanged();
    }

    public async Task ResumeAsync(CancellationToken cancellationToken = default)
    {
        if (_activeProvider is not { } provider || State != BreakMusicState.Paused)
            return;

        await provider.ResumeAsync(cancellationToken);

        State = BreakMusicState.Playing;

        InvokeStateChanged();
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_activeProvider is not { } provider)
            return;

        await provider.StopAsync(cancellationToken: cancellationToken);

        State = BreakMusicState.Stopped;
        _suspendedFromPlaying = false;

        InvokeStateChanged();
    }

    public async Task SkipAsync(CancellationToken cancellationToken = default)
    {
        if (_activeProvider is not { } provider || State == BreakMusicState.Stopped)
            return;

        await provider.SkipAsync(cancellationToken);

        InvokeStateChanged();
    }

    public async Task SetVolumeAsync(float volume, CancellationToken cancellationToken = default)
    {
        Volume = Math.Clamp(volume, 0f, 1f);

        if (_activeProvider is { } provider)
            await provider.SetVolumeAsync(Volume, cancellationToken);

        InvokeStateChanged();
    }

    public async Task SuspendAsync(CancellationToken cancellationToken = default)
    {
        // Only playback is interrupted. Paused and Stopped are where a host put it, and coming
        // back on the song's behalf would override them.
        if (_activeProvider is not { } provider || State != BreakMusicState.Playing)
            return;

        await provider.StopAsync(SuspendFade, cancellationToken);

        State = BreakMusicState.Suspended;
        _suspendedFromPlaying = true;

        Logger.LogInformation("Break music suspended for something with its own audio");

        InvokeStateChanged();
    }

    public async Task RestoreAsync(CancellationToken cancellationToken = default)
    {
        if (State != BreakMusicState.Suspended || !_suspendedFromPlaying)
            return;

        _suspendedFromPlaying = false;

        // Started rather than resumed: the suspend stopped the provider outright, because a bed
        // held open across a whole song is a transcode running for nobody.
        if (!await StartAsync(cancellationToken))
        {
            State = BreakMusicState.Stopped;
            InvokeStateChanged();
        }
    }

    public void Dispose()
    {
        foreach (var provider in _providers)
            provider.TrackChanged -= OnProviderTrackChanged;

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

        return _providers.FirstOrDefault();
    }

    private void OnProviderTrackChanged(object? sender, EventArgs e)
    {
        // Only the provider the venue chose speaks for the console; another one still winding
        // down would otherwise redraw the panel with its own track.
        if (!ReferenceEquals(sender, _activeProvider))
            return;

        InvokeStateChanged();
    }
}
