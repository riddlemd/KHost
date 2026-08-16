using Microsoft.AspNetCore.Components;
using KHost.Abstractions.Services.IPC;
using KHost.UserInterface.Services;

namespace KHost.UserInterface.Components;

public partial class ScreensButton : IDisposable
{
    [Inject] private IDialogService? DialogService { get; set; }
    [Inject] private IScreenServer? ScreenServer { get; set; }

    private int _screenCount;

    private string Title => _screenCount switch
    {
        0 => "Screens — none connected",
        1 => "Screens — 1 connected",
        _ => $"Screens — {_screenCount} connected",
    };

    protected override async Task OnInitializedAsync()
    {
        ScreenServer!.ScreenConnected += OnScreensChanged;
        ScreenServer.ScreenDisconnected += OnScreensChanged;

        await RefreshCountAsync();
    }

    // Hub callbacks arrive off the render thread, so re-count and marshal back.
    private void OnScreensChanged(object? sender, ScreenConnectionEventArgs e) =>
        _ = InvokeAsync(RefreshCountAsync);

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
        if (ScreenServer is null) return;

        ScreenServer.ScreenConnected -= OnScreensChanged;
        ScreenServer.ScreenDisconnected -= OnScreensChanged;
    }
}
