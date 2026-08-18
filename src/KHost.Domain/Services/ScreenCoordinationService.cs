using KHost.Abstractions.Services;
using KHost.Abstractions.Services.IPC;
using Microsoft.Extensions.Logging;

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
    private readonly SemaphoreSlim _lock = new(1, 1);

    // Screens the user has pinned on or off. Absent means "follow the audio role".
    private readonly Dictionary<string, bool> _audioOverrides = [];

    private string? _audioScreenId;
    private string? _primaryScreenId;

    public ScreenCoordinationService(ILogger<ScreenCoordinationService> logger, IScreenServer screenServer)
        : base(logger)
    {
        _screenServer = screenServer;
        _screenServer.ScreenConnected += OnScreenConnected;
        _screenServer.ScreenDisconnected += OnScreenDisconnected;
    }

    /// <summary>A screen can register before this service is first resolved.</summary>
    public Task InitializeAsync(CancellationToken cancellationToken = default)
        => EnsureRolesAsync(cancellationToken);

    public string? AudioScreenId => _audioScreenId;

    public string? PrimaryScreenId => _primaryScreenId;

    public bool RolesAreSplit => _audioScreenId is not null && _audioScreenId != _primaryScreenId;

    public bool IsAudioEnabled(string screenId)
        => _audioOverrides.TryGetValue(screenId, out var enabled)
            ? enabled
            : screenId == _audioScreenId;

    public bool HasAudioOverride(string screenId) => _audioOverrides.ContainsKey(screenId);

    public async Task<string?> EnsureRolesAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var screens = await ConnectedAsync();
            var previousAudio = _audioScreenId;

            // Keep the incumbent while it is still present: moving either role mid-song makes
            // every follower re-align on a different reference, a visible jump on all at once.
            if (screens.All(s => s.ScreenId != _audioScreenId))
                _audioScreenId = ElectAudioScreen(screens);

            var primary = DerivePrimaryScreen(screens);
            var changed = _audioScreenId != previousAudio || primary != _primaryScreenId;
            _primaryScreenId = primary;

            // Only when a role actually moved: this runs on every connect, and re-pushing volume
            // to screens whose answer has not changed is pure chatter.
            if (changed)
            {
                await ApplyAudioAsync(screens);

                // Only on a move: this runs on every re-anchor, which is once a second, and a
                // line a second is what buries the entries that matter.
                LogRoles();
            }

            return _audioScreenId;
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

            if (_audioScreenId == screenId) return true;

            _audioScreenId = screenId;

            // The primary follows the audio wherever it can, so this move usually takes it along.
            _primaryScreenId = DerivePrimaryScreen(screens);

            LogRoles();
            await ApplyAudioAsync(screens);
        }
        finally { _lock.Release(); }

        InvokeStateChanged();
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

        InvokeStateChanged();
    }

    public async Task ClearAudioOverrideAsync(string screenId, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (!_audioOverrides.Remove(screenId)) return;
            await ApplyAudioAsync(await ConnectedAsync());
        }
        finally { _lock.Release(); }

        InvokeStateChanged();
    }

    /// <summary>Prefers a syncable screen, so the audible one is never the corrected one.</summary>
    private static string? ElectAudioScreen(List<IScreenConnection> screens)
        => (screens.FirstOrDefault(s => s.Capabilities is { SupportsAudio: true, SupportsSync: true })
            ?? screens.FirstOrDefault(s => s.Capabilities.SupportsAudio))?.ScreenId;

    /// <summary>
    /// The audio screen whenever it can sync: correction is a seek, and seeking the screen the
    /// room hears is audible.
    /// </summary>
    private string? DerivePrimaryScreen(List<IScreenConnection> screens)
    {
        var audio = screens.FirstOrDefault(s => s.ScreenId == _audioScreenId);
        if (audio?.Capabilities.SupportsSync == true) return audio.ScreenId;

        var incumbent = screens.FirstOrDefault(
            s => s.ScreenId == _primaryScreenId && s.Capabilities.SupportsSync);
        if (incumbent is not null) return incumbent.ScreenId;

        return screens.FirstOrDefault(s => s.Capabilities.SupportsSync)?.ScreenId;
    }

    private void LogRoles()
    {
        if (RolesAreSplit)
            Logger.LogWarning(
                "Audio is on {AudioScreenId} but primary follows {PrimaryScreenId}; the synced screens "
                + "are tracking a clock the room cannot hear and may drift from it",
                _audioScreenId, _primaryScreenId);
        else
            Logger.LogInformation("Audio and primary are both on {ScreenId}", _audioScreenId ?? "(none)");
    }

    /// <summary>Caller holds the lock.</summary>
    private async Task ApplyAudioAsync(List<IScreenConnection> screens)
    {
        foreach (var screen in screens)
        {
            var volume = IsAudioEnabled(screen.ScreenId) ? AudibleVolume : MutedVolume;

            try
            {
                await _screenServer.SendCommandAsync(screen.ScreenId, new SetVolumeCommand { Volume = volume });
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to set volume on {ScreenId}", screen.ScreenId);
            }
        }
    }

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
            InvokeStateChanged();
        });

    private void OnScreenDisconnected(object? sender, ScreenConnectionEventArgs e) =>
        _ = Task.Run(async () =>
        {
            // A screen that comes back should not inherit a mute the user set on a past session.
            await ClearAudioOverrideAsync(e.Connection.ScreenId);

            if (e.Connection.ScreenId == _audioScreenId) _audioScreenId = null;
            if (e.Connection.ScreenId == _primaryScreenId) _primaryScreenId = null;

            await EnsureRolesAsync();
            InvokeStateChanged();
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

        // Deliberately not disposing _lock: a detached connect handler may still hold it.
    }
}
