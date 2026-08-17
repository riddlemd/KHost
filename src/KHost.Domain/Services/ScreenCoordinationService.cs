using KHost.Abstractions.Services;
using KHost.Abstractions.Services.IPC;
using Microsoft.Extensions.Logging;

namespace KHost.Domain.Services;

/// <summary>
/// Decides which screen the room hears and which screen the others follow — the same screen, since
/// the primary is the one that is never rate-corrected. Everything else is muted by default.
/// </summary>
public sealed class ScreenCoordinationService : BaseService, IScreenCoordinationService, IDisposable
{
    private const float AudibleVolume = 1.0f;
    private const float MutedVolume = 0.0f;

    private readonly IScreenServer _screenServer;
    private readonly SemaphoreSlim _lock = new(1, 1);

    // Screens the user has pinned on or off. Absent means "follow the primary".
    private readonly Dictionary<string, bool> _audioOverrides = [];

    private string? _primaryScreenId;

    public ScreenCoordinationService(ILogger<ScreenCoordinationService> logger, IScreenServer screenServer)
        : base(logger)
    {
        _screenServer = screenServer;
        _screenServer.ScreenConnected += OnScreenConnected;
        _screenServer.ScreenDisconnected += OnScreenDisconnected;
    }

    /// <summary>
    /// Picks up anything already connected. The constructor covers screens that arrive later, but
    /// on a restart a screen can register before this service is first resolved.
    /// </summary>
    public Task InitializeAsync(CancellationToken cancellationToken = default)
        => EnsurePrimaryAsync(cancellationToken);

    public string? PrimaryScreenId => _primaryScreenId;

    public bool IsAudioEnabled(string screenId)
        => _audioOverrides.TryGetValue(screenId, out var enabled)
            ? enabled
            : screenId == _primaryScreenId;

    public bool HasAudioOverride(string screenId) => _audioOverrides.ContainsKey(screenId);

    public async Task<string?> EnsurePrimaryAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var group = await SyncCapableScreensAsync();
            if (group.Count == 0) return _primaryScreenId = null;

            // Keep the incumbent: moving the primary mid-song makes every follower re-align on a
            // different reference, which is a visible jump on all of them at once.
            if (group.Any(s => s.ScreenId == _primaryScreenId)) return _primaryScreenId;

            var elected = group.FirstOrDefault(s => s.Capabilities.SupportsAudio) ?? group[0];
            if (!elected.Capabilities.SupportsAudio)
                Logger.LogWarning("No audio-capable screen in the sync-capable screens; {ScreenId} leads silently",
                    elected.ScreenId);

            _primaryScreenId = elected.ScreenId;
            Logger.LogInformation("Primary screen is {ScreenId}", _primaryScreenId);

            await ApplyAudioAsync();
            return _primaryScreenId;
        }
        finally { _lock.Release(); }
    }

    public async Task<bool> SetPrimaryAsync(string screenId, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var group = await SyncCapableScreensAsync();

            // A loose consumer plays on its own schedule, so it cannot define one for anyone else.
            if (group.All(s => s.ScreenId != screenId))
            {
                Logger.LogWarning("Refused to make {ScreenId} primary: not in the sync-capable screens", screenId);
                return false;
            }

            if (_primaryScreenId == screenId) return true;

            _primaryScreenId = screenId;
            Logger.LogInformation("Primary screen set to {ScreenId}", screenId);

            await ApplyAudioAsync();
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
            await ApplyAudioAsync();
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
            await ApplyAudioAsync();
        }
        finally { _lock.Release(); }

        InvokeStateChanged();
    }

    /// <summary>Pushes every connected screen's volume to match the current rules. Caller holds the lock.</summary>
    private async Task ApplyAudioAsync()
    {
        await foreach (var screen in _screenServer.GetConnectedScreensAsync())
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

    private async Task<List<IScreenConnection>> SyncCapableScreensAsync()
    {
        var group = new List<IScreenConnection>();

        try
        {
            await foreach (var screen in _screenServer.GetConnectedScreensAsync())
                if (screen.Capabilities.SupportsSync)
                    group.Add(screen);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to enumerate the sync-capable screens");
        }

        return group;
    }

    // Raised while ScreenServerService holds the same lock GetConnectedScreensAsync waits on, so
    // this work has to leave the hub thread or it deadlocks.
    private void OnScreenConnected(object? sender, ScreenConnectionEventArgs e) =>
        _ = Task.Run(async () =>
        {
            // A new screen arrives unmuted; without this it would add a second voice to the room.
            await EnsurePrimaryAsync();
            await MuteUnlessPermittedAsync(e.Connection.ScreenId);
            InvokeStateChanged();
        });

    private void OnScreenDisconnected(object? sender, ScreenConnectionEventArgs e) =>
        _ = Task.Run(async () =>
        {
            // A screen that comes back should not inherit a mute the user set on a past session.
            await ClearAudioOverrideAsync(e.Connection.ScreenId);

            if (e.Connection.ScreenId == _primaryScreenId)
            {
                _primaryScreenId = null;
                await EnsurePrimaryAsync();
            }

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
