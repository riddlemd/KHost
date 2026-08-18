using KHost.Abstractions.Services;
using KHost.Abstractions.Services.IPC;
using KHost.UserInterface.Models;
using Microsoft.AspNetCore.Components;

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
    private Dictionary<string, ScreenPlaybackState> _screenStates = [];
    private string _screenName = "";
    private string _selectedProviderName = "";

    private string? _busyCastDevice;
    private string? _castError;

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
        _busyCastDevice = device.Id;

        try { await Cast!.DisconnectAsync(); }
        finally
        {
            _busyCastDevice = null;
            StateHasChanged();
        }
    }

    private bool IsAudioScreen(IScreenConnection screen) => ScreenCoordination!.AudioScreenId == screen.ScreenId;

    private bool IsPrimaryScreen(IScreenConnection screen) => ScreenCoordination!.PrimaryScreenId == screen.ScreenId;

    private bool IsAudible(IScreenConnection screen) => ScreenCoordination!.IsAudioEnabled(screen.ScreenId);

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
