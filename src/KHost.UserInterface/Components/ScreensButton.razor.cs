using Microsoft.AspNetCore.Components;
using KHost.Abstractions.Services;
using KHost.Abstractions.Services.IPC;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using KHost.UserInterface.Services;

namespace KHost.UserInterface.Components;

public partial class ScreensButton : IDisposable
{
    [Inject] private IDialogService? DialogService { get; set; }
    [Inject] private IScreenServer? ScreenServer { get; set; }
    [Inject] private IEnumerable<IDisplayProvider> DisplayProviders { get; set; } = [];

    private IDisplayProvider? Display => DisplayProviders.FirstOrDefault();
    [Inject] private IMessageBroker Broker { get; set; } = default!;

    private readonly SubscriptionSet _subscriptions = new();

    internal int _screenCount;

    /// <summary>A provider's device is not a screen, but the room is still watching something.</summary>
    internal bool IsSendingToDevice => Display?.ConnectedDeviceId is { Length: > 0 };

    internal bool IsActive => _screenCount > 0 || IsSendingToDevice;

    internal string Title
    {
        get
        {
            var screens = _screenCount switch
            {
                0 => "none connected",
                1 => "1 connected",
                _ => $"{_screenCount} connected",
            };

            if (!IsSendingToDevice) return $"Screens: {screens}";

            // The provider names itself, so no transport's wording is built in here.
            var device = Display!.Devices.FirstOrDefault(d => d.IsConnected)?.Name ?? Display.Name;
            return $"Screens: {screens}, showing on {device}";
        }
    }

    protected override async Task OnInitializedAsync()
    {
        ScreenServer!.ScreenConnected += OnScreensChanged;
        ScreenServer.ScreenDisconnected += OnScreensChanged;

        _subscriptions.Add(Broker.Subscribe<DisplaysChanged>(OnDisplaysChanged));

        await RefreshCountAsync();
    }

    // Hub callbacks arrive off the render thread, so re-count and marshal back.
    private void OnScreensChanged(object? sender, ScreenConnectionEventArgs e) =>
        _ = InvokeAsync(RefreshCountAsync);

    // Connecting a device changes no screen, so the colour needs its own trigger.
    private void OnDisplaysChanged(DisplaysChanged message) => _ = InvokeAsync(StateHasChanged);

    private async Task RefreshCountAsync()
    {
        var count = 0;
        await foreach (var _ in ScreenServer!.GetConnectedScreensAsync())
            count++;

        _screenCount = count;
        StateHasChanged();
    }

    private Task OpenAsync() => DialogService!.ShowScreensAsync();

    public void Dispose()
    {
        _subscriptions.Dispose();

        if (ScreenServer is null) return;

        ScreenServer.ScreenConnected -= OnScreensChanged;
        ScreenServer.ScreenDisconnected -= OnScreensChanged;
    }
}
