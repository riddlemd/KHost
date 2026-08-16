using KHost.Abstractions.Services.IPC;
using KHost.UserInterface.Models;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Dialogs;

public partial class ScreensDialog : IDisposable
{
    [Inject] private IScreenServer? ScreenServer { get; set; }
    [Inject] private IEnumerable<IScreenProvider>? ScreenProviders { get; set; }

    [Parameter] public bool IsOpen { get; set; }
    [Parameter] public EventCallback OnClose { get; set; }

    private List<IScreenConnection> _connectedScreens = [];
    private List<IScreenProvider> _providers = [];
    private Dictionary<string, ScreenPlaybackState> _screenStates = [];
    private string _screenName = "";
    private string _selectedProviderName = "";

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
    }

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
