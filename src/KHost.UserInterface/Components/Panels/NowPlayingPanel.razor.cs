using Microsoft.AspNetCore.Components;
using KHost.Abstractions.Services;
using KHost.UserInterface.Services;

namespace KHost.UserInterface.Components.Panels;

public partial class NowPlayingPanel : IDisposable
{
    [Inject] private IPlaybackService? PlaybackService { get; set; }
    [Inject] private ISingerQueueService? SingerQueueService { get; set; }
    [Inject] private IDialogService? DialogService { get; set; }

    protected override void OnInitialized() =>
        PlaybackService?.StateChanged += OnStateChanged;

    private async Task PlayAsync()
    {
        if (!await PlaybackService!.HasConnectedScreenAsync())
        {
            await DialogService!.ShowNoScreensAsync();
            return;
        }

        await PlaybackService.PlayAsync();
    }

    private void OnStateChanged(object? sender, EventArgs e) => InvokeAsync(StateHasChanged);

    private static string FormatTime(TimeSpan ts) =>
        $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}";

    public void Dispose() =>
        PlaybackService?.StateChanged -= OnStateChanged;
}
