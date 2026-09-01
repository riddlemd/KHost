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
    [Inject] private ICastService? Cast { get; set; }
    [Inject] private IMessageBroker Broker { get; set; } = default!;

    private readonly SubscriptionSet _subscriptions = new();

    internal int _screenCount;

    /// <summary>A Cast receiver is not a screen, but the room is still watching something.</summary>
    internal bool IsCasting => Cast?.ConnectedDeviceId is { Length: > 0 };

    internal bool IsActive => _screenCount > 0 || IsCasting;

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

            if (!IsCasting) return $"Screens — {screens}";

            var receiver = Cast!.Devices.FirstOrDefault(d => d.IsConnected)?.Name ?? "a Cast receiver";
            return $"Screens — {screens}, casting to {receiver}";
        }
    }

    protected override async Task OnInitializedAsync()
    {
        ScreenServer!.ScreenConnected += OnScreensChanged;
        ScreenServer.ScreenDisconnected += OnScreensChanged;

        _subscriptions.Add(Broker.Subscribe<CastChanged>(OnCastChanged));

        await RefreshCountAsync();
    }

    // Hub callbacks arrive off the render thread, so re-count and marshal back.
    private void OnScreensChanged(object? sender, ScreenConnectionEventArgs e) =>
        _ = InvokeAsync(RefreshCountAsync);

    // Connecting a receiver changes no screen, so the colour needs its own trigger.
    private void OnCastChanged(CastChanged message) => _ = InvokeAsync(StateHasChanged);

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
