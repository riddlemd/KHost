using KHost.Abstractions.Services;
using KHost.Abstractions.Services.IPC;
using KHost.UserInterface.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace KHost.UserInterface.Components.Dialogs;

public partial class ScreensDialog : IDisposable
{
    [Inject] private IScreenServer? ScreenServer { get; set; }
    [Inject] private IScreenCoordinationService? ScreenCoordination { get; set; }
    [Inject] private ICastService? Cast { get; set; }
    [Inject] private IEnumerable<IScreenProvider>? ScreenProviders { get; set; }

    [Parameter] public bool IsOpen { get; set; }
    [Parameter] public EventCallback OnClose { get; set; }

    private List<IScreenConnection> _connectedScreens = [];
    private List<IScreenProvider> _providers = [];
    internal Dictionary<string, ScreenPlaybackState> _screenStates = [];
    private string _screenName = "";
    private string _selectedProviderName = "";

    private string? _busyCastDevice;
    private string? _castError;
    private bool _castPickerOpen;
    private bool _castSearchBusy;

    private bool _isLaunching;
    private string? _pendingScreenId;
    private CancellationTokenSource? _launchCts;

    private bool CanLaunch =>
        !_isLaunching &&
        _providers.FirstOrDefault(p => p.Name == _selectedProviderName)?.IsAvailable == true;

    protected override async Task OnInitializedAsync()
    {
        _providers = ScreenProviders!.ToList();
        _selectedProviderName = _providers.FirstOrDefault()?.Name ?? "";

        await foreach (var screen in ScreenServer!.GetConnectedScreensAsync())
            _connectedScreens.Add(screen);

        ScreenServer.ScreenConnected += OnScreenConnected;
        ScreenServer.ScreenDisconnected += OnScreenDisconnected;
        ScreenServer.StateReceived += OnStateReceived;
        ScreenCoordination!.StateChanged += OnScreenCoordinationChanged;
        Cast!.StateChanged += OnScreenCoordinationChanged;
    }

    // A receiver is never a screen, so it never moves up into the connected screens.
    private IReadOnlyList<CastDevice> CastDevices => Cast?.Devices ?? [];

    internal bool IsSearchingForCast => Cast?.IsDiscovering == true;

    /// <summary>
    /// Discovery is off at startup and lives only for this run — browsing sweeps the whole network,
    /// so it should be something the host turns on for as long as they need it.
    /// </summary>
    internal async Task ToggleCastSearchAsync()
    {
        if (_castSearchBusy) return;

        _castSearchBusy = true;
        _castError = null;

        try
        {
            if (IsSearchingForCast)
            {
                _castPickerOpen = false;
                await Cast!.StopDiscoveryAsync();
            }
            else
            {
                await Cast!.StartDiscoveryAsync();
            }
        }
        catch (Exception)
        {
            _castError = "Could not search for Cast receivers.";
        }
        finally
        {
            _castSearchBusy = false;
            StateHasChanged();
        }
    }

    private CastDevice? ConnectedCastDevice => CastDevices.FirstOrDefault(d => d.IsConnected);

    private string CastTriggerText => ConnectedCastDevice is { } connected
        ? $"Casting to {connected.Name}"
        : "Not casting";

    private void ToggleCastPicker() => _castPickerOpen = !_castPickerOpen;

    private void CloseCastPicker() => _castPickerOpen = false;

    private async Task OnCastPickerKeyDownAsync(KeyboardEventArgs e)
    {
        if (e.Key != "Escape" || !_castPickerOpen) return;

        _castPickerOpen = false;
        await Task.CompletedTask;
    }

    /// <summary>Picking the receiver already casting is a no-op — Stop is what disconnects.</summary>
    private async Task SelectCastAsync(CastDevice device)
    {
        _castPickerOpen = false;

        if (device.IsConnected) return;
        await ConnectCastAsync(device);
    }

    private async Task ConnectCastAsync(CastDevice device)
    {
        _castError = null;
        _busyCastDevice = device.Id;

        try
        {
            if (!await Cast!.ConnectAsync(device.Id))
                _castError = $"Could not reach {device.Name}.";
        }
        finally
        {
            _busyCastDevice = null;
            StateHasChanged();
        }
    }

    private async Task DisconnectCastAsync(CastDevice device)
    {
        _castPickerOpen = false;
        _busyCastDevice = device.Id;

        try { await Cast!.DisconnectAsync(); }
        finally
        {
            _busyCastDevice = null;
            StateHasChanged();
        }
    }

    /// <summary>
    /// Sync is relative, so the primary is the reference rather than a participant — it reads as
    /// in sync by definition, and everyone else is judged against it.
    /// </summary>
    public enum SyncStatus { NotSupported, Unknown, Reference, InSync, Drifting }

    // Looser than the screen-side realign threshold (0.15s), so a screen the correction loop is
    // already pulling back does not flicker a warning on the way.
    private static readonly TimeSpan SyncTolerance = TimeSpan.FromMilliseconds(250);

    internal SyncStatus GetSyncStatus(IScreenConnection screen) => GetSyncStatus(screen, out _);

    internal SyncStatus GetSyncStatus(IScreenConnection screen, out TimeSpan drift)
    {
        drift = TimeSpan.Zero;

        if (!screen.Capabilities.SupportsSync) return SyncStatus.NotSupported;

        var primaryId = ScreenCoordination!.PrimaryScreenId;
        if (primaryId is null) return SyncStatus.Unknown;
        if (screen.ScreenId == primaryId) return SyncStatus.Reference;

        if (!TryProjectPosition(screen.ScreenId, out var here)) return SyncStatus.Unknown;
        if (!TryProjectPosition(primaryId, out var reference)) return SyncStatus.Unknown;

        drift = here - reference;
        return drift.Duration() <= SyncTolerance ? SyncStatus.InSync : SyncStatus.Drifting;
    }

    /// <summary>
    /// Rolls the last reported position forward to now. Without SampledAtUtc the report carries an
    /// unknown delivery latency, which would read as drift the screen does not have.
    /// </summary>
    private bool TryProjectPosition(string screenId, out TimeSpan position)
    {
        position = TimeSpan.Zero;

        if (!_screenStates.TryGetValue(screenId, out var state)) return false;
        if (!state.IsPlaying || state.SampledAtUtc is not { } sampled) return false;

        position = state.Position + (DateTime.UtcNow - sampled);
        return true;
    }

    /// <summary>
    /// One glyph for every state a screen can actually be in — colour carries the state, so the
    /// column reads as a single column rather than four unrelated symbols. A screen that can never
    /// sync is the exception: that is a different fact, not a worse value of the same one.
    /// </summary>
    internal string SyncIcon(IScreenConnection screen) =>
        GetSyncStatus(screen) == SyncStatus.NotSupported ? "bi-slash-circle" : "bi-diagram-3";

    private string SyncModifier(IScreenConnection screen) => GetSyncStatus(screen) switch
    {
        SyncStatus.InSync or SyncStatus.Reference => "kh-screens-dialog__sync--ok",
        SyncStatus.Drifting => "kh-screens-dialog__sync--drifting",
        _ => "kh-screens-dialog__sync--idle",
    };

    internal string SyncTitle(IScreenConnection screen)
    {
        var status = GetSyncStatus(screen, out var drift);
        var milliseconds = Math.Abs(drift.TotalMilliseconds).ToString("F0");

        return status switch
        {
            SyncStatus.NotSupported => "Plays on its own schedule, so it cannot be held to the group timeline.",
            SyncStatus.Reference => "In sync — this is the primary the others are held to.",
            SyncStatus.InSync => $"In sync — {milliseconds} ms from the primary.",
            SyncStatus.Drifting => drift > TimeSpan.Zero
                ? $"Drifting — {milliseconds} ms ahead of the primary."
                : $"Drifting — {milliseconds} ms behind the primary.",
            _ => "Nothing playing, so there is no timeline to be on.",
        };
    }

    private bool IsAudioScreen(IScreenConnection screen) => ScreenCoordination!.AudioScreenId == screen.ScreenId;

    private bool IsPrimaryScreen(IScreenConnection screen) => ScreenCoordination!.PrimaryScreenId == screen.ScreenId;

    private bool IsAudible(IScreenConnection screen) => ScreenCoordination!.IsAudioEnabled(screen.ScreenId);

    private bool IsVideoOn(IScreenConnection screen) => ScreenCoordination!.IsVideoEnabled(screen.ScreenId);

    private string AudioTitle(IScreenConnection screen) => !screen.Capabilities.SupportsAudio
        ? "This screen renders no audio."
        : IsAudible(screen) ? "Audible — click to mute" : "Muted — click to unmute";

    private string PrimaryTitle(IScreenConnection screen) => !screen.Capabilities.SupportsSync
        ? "Plays on its own schedule, so it cannot be the primary."
        : "Make this the primary. The audio moves with it.";

    private async Task ToggleVideoAsync(IScreenConnection screen)
    {
        await ScreenCoordination!.SetVideoEnabledAsync(screen.ScreenId, !IsVideoOn(screen));
        StateHasChanged();
    }

    private async Task SetAsPrimaryAsync(IScreenConnection screen)
    {
        await ScreenCoordination!.SetAudioScreenAsync(screen.ScreenId);
        StateHasChanged();
    }

    /// <summary>Toggling back to the default drops the override rather than pinning it.</summary>
    private async Task ToggleAudioAsync(IScreenConnection screen)
    {
        var wanted = !IsAudible(screen);

        if (wanted == (screen.ScreenId == ScreenCoordination!.AudioScreenId))
            await ScreenCoordination.ClearAudioOverrideAsync(screen.ScreenId);
        else
            await ScreenCoordination.SetAudioEnabledAsync(screen.ScreenId, wanted);

        StateHasChanged();
    }

    private void OnScreenCoordinationChanged(object? sender, EventArgs e) => InvokeAsync(StateHasChanged);

    private void OnScreenConnected(object? sender, ScreenConnectionEventArgs e)
    {
        _connectedScreens.Add(e.Connection);

        if (_isLaunching && e.Connection.ScreenId == _pendingScreenId)
            ClearLaunching();

        InvokeAsync(StateHasChanged);
    }

    private void OnScreenDisconnected(object? sender, ScreenConnectionEventArgs e)
    {
        _connectedScreens.RemoveAll(s => s.ScreenId == e.Connection.ScreenId);
        _screenStates.Remove(e.Connection.ScreenId);
        InvokeAsync(StateHasChanged);
    }

    private void OnStateReceived(object? sender, ScreenStateReceivedEventArgs e)
    {
        if (e.State is ScreenPlaybackState state)
            _screenStates[e.ScreenId] = state;
        InvokeAsync(StateHasChanged);
    }

    private async Task LaunchAsync()
    {
        if (_isLaunching) return;

        var provider = _providers.FirstOrDefault(p => p.Name == _selectedProviderName);
        if (provider is null || !provider.IsAvailable) return;

        var name = string.IsNullOrWhiteSpace(_screenName) ? GenerateScreenName() : _screenName.Trim();

        _pendingScreenId = name;
        _isLaunching = true;
        _screenName = "";

        try
        {
            await provider.LaunchAsync(name);
        }
        catch (Exception)
        {
            ClearLaunching(); // process failed to start
            return;
        }

        // Keep "Loading…" until the screen connects (OnScreenConnected cancels the CTS)
        // or the timeout elapses.
        _launchCts = new CancellationTokenSource();
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(15), _launchCts.Token);
        }
        catch (TaskCanceledException)
        {
            return; // connected — already cleared
        }

        if (_isLaunching && _pendingScreenId == name)
            ClearLaunching(); // timed out without connecting
    }

    private void ClearLaunching()
    {
        _isLaunching = false;
        _pendingScreenId = null;
        _launchCts?.Cancel(); // releases the Task.Delay in LaunchAsync
        InvokeAsync(StateHasChanged);
    }

    private string GenerateScreenName()
    {
        var taken = _connectedScreens.Select(s => s.ScreenId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (_pendingScreenId is not null) taken.Add(_pendingScreenId);

        for (var i = 1; ; i++)
        {
            var name = $"Screen {i}";
            if (!taken.Contains(name)) return name;
        }
    }

    public void Dispose()
    {
        _launchCts?.Dispose();

        if (ScreenCoordination is not null) ScreenCoordination.StateChanged -= OnScreenCoordinationChanged;
        if (Cast is not null) Cast.StateChanged -= OnScreenCoordinationChanged;

        if (ScreenServer is null) return;
        ScreenServer.ScreenConnected -= OnScreenConnected;
        ScreenServer.ScreenDisconnected -= OnScreenDisconnected;
        ScreenServer.StateReceived -= OnStateReceived;
    }

    public record DialogRequest : BaseDialogRequest
    {
        public DialogRequest(Action? onClose) : base(onClose) { }
    }
}
