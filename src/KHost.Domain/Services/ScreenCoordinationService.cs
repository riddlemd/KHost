using KHost.Abstractions.Services;
using KHost.Abstractions.Services.IPC;
using KHost.Plugins.Sdk.Messaging;
using KHost.Plugins.Sdk.Messaging.Messages;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace KHost.Domain.Services;

/// <summary>
/// Decides which screen the room hears and which the others are held to. Everything else is
/// muted: two screens playing the same song into one room fight each other.
/// </summary>
public sealed class ScreenCoordinationService : BaseService, IScreenCoordinationService, IDisposable
{
    private const float AudibleVolume = 1.0f;
    private const float MutedVolume = 0.0f;

    private readonly IScreenServer _screenServer;
    private readonly IMessageBroker _broker;
    private readonly IVenuesService _venuesService;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly IDisposable _venueChanged;

    // Screens the user has pinned on or off. Absent means "follow the audio role".
    // Concurrent, not plain: the writers below hold _lock, but IsAudioEnabled/IsVideoEnabled are
    // synchronous and read straight from a Razor render while a disconnect mutates on a hub thread.
    private readonly ConcurrentDictionary<string, bool> _audioOverrides = new();

    // Only the blanked ones are tracked; a screen that can render is rendering unless told not to.
    private readonly ConcurrentDictionary<string, bool> _videoDisabled = new();

    // One reference, swapped whole: readers poll these lock-free from other threads (Razor
    // renders, PlaybackService), and two separate fields let them observe one role vacated
    // while the other still names a screen that just left.
    private volatile RoleSnapshot _roles = new(null, null);

    public ScreenCoordinationService(ILogger<ScreenCoordinationService> logger, IScreenServer screenServer, IVenuesService venuesService, IMessageBroker broker)
        : base(logger)
    {
        _broker = broker;
        _screenServer = screenServer;
        _venuesService = venuesService;
        _screenServer.ScreenConnected += OnScreenConnected;
        _screenServer.ScreenDisconnected += OnScreenDisconnected;
        // Selecting or editing a venue changes what "audible" means; without this the new
        // volume waits for the next role move to be heard.
        _venueChanged = broker.Subscribe<VenuesChanged>(OnVenueStateChanged);
    }

    /// <summary>A screen can register before this service is first resolved.</summary>
    public Task InitializeAsync(CancellationToken cancellationToken = default)
        => EnsureRolesAsync(cancellationToken);

    public string? AudioScreenId => _roles.Audio;

    public string? PrimaryScreenId => _roles.Primary;

    public bool RolesAreSplit => _roles is { Audio: not null } r && r.Audio != r.Primary;

    public bool IsAudioEnabled(string screenId)
        => _audioOverrides.TryGetValue(screenId, out var enabled)
            ? enabled
            : screenId == _roles.Audio;

    public bool HasAudioOverride(string screenId) => _audioOverrides.ContainsKey(screenId);

    public bool IsVideoEnabled(string screenId) => !_videoDisabled.ContainsKey(screenId);

    public async Task SetVideoEnabledAsync(string screenId, bool enabled, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (enabled) _videoDisabled.TryRemove(screenId, out _);
            else _videoDisabled[screenId] = true;

            try
            {
                await _screenServer.SendCommandAsync(screenId, new SetVideoCommand { Enabled = enabled });
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to set video on {ScreenId}", screenId);
            }
        }
        finally { _lock.Release(); }

        _broker.Announce(new ScreensChanged());
    }

    public async Task<string?> EnsureRolesAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var screens = await ConnectedAsync();
            var previous = _roles;

            // Keep the incumbent while it is still present: moving either role mid-song makes
            // every follower re-align on a different reference, a visible jump on all at once.
            var audio = screens.Any(s => s.ScreenId == previous.Audio)
                ? previous.Audio
                : ElectAudioScreen(screens);

            var primary = DerivePrimaryScreen(screens, audio, previous.Primary);
            var changed = audio != previous.Audio || primary != previous.Primary;
            _roles = new RoleSnapshot(audio, primary);

            // Only when a role actually moved: this runs on every connect, and re-pushing volume
            // to screens whose answer has not changed is pure chatter.
            if (changed)
            {
                await ApplyAudioAsync(screens);

                // Only on a move: this runs on every re-anchor, which is once a second, and a
                // line a second is what buries the entries that matter.
                LogRoles();
            }

            return _roles.Audio;
        }
        finally { _lock.Release(); }
    }

    public async Task<bool> SetAudioScreenAsync(string screenId, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var screens = await ConnectedAsync();
            var target = screens.FirstOrDefault(s => s.ScreenId == screenId);

            if (target is null || !target.Capabilities.SupportsAudio)
            {
                Logger.LogWarning("Refused to send audio to {ScreenId}: it renders no audio", screenId);
                return false;
            }

            if (_roles.Audio == screenId) return true;

            // The primary follows the audio wherever it can, so this move usually takes it along.
            _roles = new RoleSnapshot(screenId, DerivePrimaryScreen(screens, screenId, _roles.Primary));

            LogRoles();
            await ApplyAudioAsync(screens);
        }
        finally { _lock.Release(); }

        _broker.Announce(new ScreensChanged());
        return true;
    }

    public async Task SetAudioEnabledAsync(string screenId, bool enabled, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            _audioOverrides[screenId] = enabled;
            await ApplyAudioAsync(await ConnectedAsync());
        }
        finally { _lock.Release(); }

        _broker.Announce(new ScreensChanged());
    }

    public async Task ClearAudioOverrideAsync(string screenId, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (!_audioOverrides.TryRemove(screenId, out _)) return;
            await ApplyAudioAsync(await ConnectedAsync());
        }
        finally { _lock.Release(); }

        _broker.Announce(new ScreensChanged());
    }

    /// <summary>Prefers a syncable screen, so the audible one is never the corrected one.</summary>
    private static string? ElectAudioScreen(List<IScreenConnection> screens)
        => (screens.FirstOrDefault(s => s.Capabilities is { SupportsAudio: true, SupportsSync: true })
            ?? screens.FirstOrDefault(s => s.Capabilities.SupportsAudio))?.ScreenId;

    /// <summary>
    /// The audio screen whenever it can sync: correction is a seek, and seeking the screen the
    /// room hears is audible.
    /// </summary>
    private static string? DerivePrimaryScreen(List<IScreenConnection> screens, string? audioScreenId, string? incumbentPrimaryId)
    {
        var audio = screens.FirstOrDefault(s => s.ScreenId == audioScreenId);
        if (audio?.Capabilities.SupportsSync == true) return audio.ScreenId;

        var incumbent = screens.FirstOrDefault(
            s => s.ScreenId == incumbentPrimaryId && s.Capabilities.SupportsSync);
        if (incumbent is not null) return incumbent.ScreenId;

        return screens.FirstOrDefault(s => s.Capabilities.SupportsSync)?.ScreenId;
    }

    private sealed record RoleSnapshot(string? Audio, string? Primary);

    private void LogRoles()
    {
        if (RolesAreSplit)
            Logger.LogWarning(
                "Audio is on {AudioScreenId} but primary follows {PrimaryScreenId}; the synced screens "
                + "are tracking a clock the room cannot hear and may drift from it",
                _roles.Audio, _roles.Primary);
        else
            Logger.LogInformation("Audio and primary are both on {ScreenId}", _roles.Audio ?? "(none)");
    }

    /// <summary>Caller holds the lock.</summary>
    private async Task ApplyAudioAsync(List<IScreenConnection> screens)
    {
        var audible = await AudibleVolumeAsync();

        foreach (var screen in screens)
        {
            var volume = IsAudioEnabled(screen.ScreenId) ? audible : MutedVolume;

            try
            {
                await _screenServer.SendCommandAsync(screen.ScreenId, new SetVolumeCommand { Volume = volume });

                // The bed and an ad's own voiceover ride the second channel, and they are the same
                // room through the same mixer — so one venue level covers both rather than leaving
                // the host balancing two. This is the only place that sets it, and it re-runs on a
                // venue edit, a role change and a screen connecting.
                await _screenServer.SendCommandAsync(screen.ScreenId, new SetBackgroundVolumeCommand { Volume = volume });
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to set volume on {ScreenId}", screen.ScreenId);
            }
        }
    }

    /// <summary>The selected venue's volume, or full volume before any venue exists.</summary>
    private async Task<float> AudibleVolumeAsync()
    {
        var venue = await _venuesService.ReadSelectedVenueAsync();

        return venue is null ? AudibleVolume : Math.Clamp(venue.Settings.DefaultVolume, 0, 100) / 100f;
    }

    private void OnVenueStateChanged(VenuesChanged message) =>
        _ = Task.Run(async () =>
        {
            await _lock.WaitAsync();
            try { await ApplyAudioAsync(await ConnectedAsync()); }
            finally { _lock.Release(); }
        });

    private async Task<List<IScreenConnection>> ConnectedAsync()
    {
        var screens = new List<IScreenConnection>();

        try
        {
            await foreach (var screen in _screenServer.GetConnectedScreensAsync())
                screens.Add(screen);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to enumerate connected screens");
        }

        return screens;
    }

    // Raised while ScreenServerService holds the same lock GetConnectedScreensAsync waits on, so
    // this work has to leave the hub thread or it deadlocks.
    private void OnScreenConnected(object? sender, ScreenConnectionEventArgs e) =>
        _ = Task.Run(async () =>
        {
            // A new screen arrives unmuted; without this it would add a second voice to the room.
            await EnsureRolesAsync();
            await MuteUnlessPermittedAsync(e.Connection.ScreenId);
            _broker.Announce(new ScreensChanged());
        });

    private void OnScreenDisconnected(object? sender, ScreenConnectionEventArgs e) =>
        _ = Task.Run(async () =>
        {
            // A screen that comes back should not inherit a mute the user set on a past session.
            await ClearAudioOverrideAsync(e.Connection.ScreenId);
            _videoDisabled.TryRemove(e.Connection.ScreenId, out _);

            // No role write here: EnsureRolesAsync drops a departed screen under the lock, and
            // a shortcut outside it is how a reader once saw audio vacated with primary still set.
            await EnsureRolesAsync();
            _broker.Announce(new ScreensChanged());
        });

    private async Task MuteUnlessPermittedAsync(string screenId)
    {
        if (IsAudioEnabled(screenId)) return;

        try
        {
            await _screenServer.SendCommandAsync(screenId, new SetVolumeCommand { Volume = MutedVolume });
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to mute {ScreenId} on connect", screenId);
        }
    }

    public void Dispose()
    {
        _screenServer.ScreenConnected -= OnScreenConnected;
        _screenServer.ScreenDisconnected -= OnScreenDisconnected;
        _venueChanged.Dispose();

        // Deliberately not disposing _lock: a detached connect handler may still hold it.
    }
}
