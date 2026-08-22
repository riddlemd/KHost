using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using KHost.Abstractions.Services;
using KHost.UserInterface.Services;

namespace KHost.UserInterface.Components.Panels;

public partial class NowPlayingPanel : IDisposable
{
    [Inject] private IPlaybackService? PlaybackService { get; set; }
    [Inject] private ISingerQueueService? SingerQueueService { get; set; }
    [Inject] private IDialogService? DialogService { get; set; }
    [Inject] private IJSRuntime JS { get; set; } = default!;

    private ElementReference _trackRef;
    private IJSObjectReference? _seekBar;

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

    private async Task SeekToClickAsync(MouseEventArgs e)
    {
        if (PlaybackService?.CurrentMedia?.Duration is not { } duration)
            return;

        _seekBar ??= await JS.InvokeAsync<IJSObjectReference>("import", "/js/seek-bar.js");

        var fraction = await _seekBar.InvokeAsync<double>("fractionFromClick", _trackRef, e.ClientX);

        await PlaybackService.SeekAsync(duration * fraction);
    }

    private void OnStateChanged(object? sender, EventArgs e) => InvokeAsync(StateHasChanged);

    private static string FormatTime(TimeSpan ts) =>
        $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}";

    public void Dispose() =>
        PlaybackService?.StateChanged -= OnStateChanged;
}
