using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using KHost.Abstractions.Services;
using KHost.Plugins.Sdk.Messaging;
using KHost.Plugins.Sdk.Messaging.Messages;
using KHost.UserInterface.Services;

namespace KHost.UserInterface.Components.Panels;

public partial class NowPlayingPanel : IDisposable
{
    [Inject] private IPlaybackService? PlaybackService { get; set; }
    [Inject] private ISingerQueueService? SingerQueueService { get; set; }
    [Inject] private IDialogService? DialogService { get; set; }
    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private IMessageBroker Broker { get; set; } = default!;

    private readonly SubscriptionSet _subscriptions = new();

    private ElementReference _trackRef;
    private IJSObjectReference? _seekBar;

    protected override void OnInitialized()
    {
        if (PlaybackService is null) return;

        _subscriptions.Add(Broker.Subscribe<PlaybackChanged>(_ => OnStateChanged(null, EventArgs.Empty)));

        // The only panel that takes the position clock: it draws the playhead, and a redraw is all
        // it does with either event.
        PlaybackService.PositionChanged += OnStateChanged;
    }

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

    private Task AdjustPitchAsync(int delta)
        => PlaybackService!.SetPitchAsync(PlaybackService.Pitch + delta);

    /// <summary>
    /// Panel-scoped like the queue's arrows: matched in Blazor, so these keys must not reach the
    /// circuit while a host is typing a singer's name.
    /// </summary>
    private Task OnKeyDownAsync(KeyboardEventArgs e)
    {
        if (PlaybackService?.CurrentPerformance is null) return Task.CompletedTask;

        // Both spellings of each physical key: the host may or may not be holding shift.
        var delta = e.Key switch
        {
            "+" or "=" => 1,
            "-" or "_" => -1,
            _ => 0,
        };

        return delta == 0 ? Task.CompletedTask : AdjustPitchAsync(delta);
    }

    private void OnStateChanged(object? sender, EventArgs e) => InvokeAsync(StateHasChanged);

    private static string FormatPitch(int semitones) =>
        semitones.ToString("+#;\u2212#;0", CultureInfo.InvariantCulture);

    private static string FormatTime(TimeSpan ts) =>
        $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}";

    public void Dispose()
    {
        _subscriptions.Dispose();

        if (PlaybackService is null) return;

        PlaybackService.PositionChanged -= OnStateChanged;
    }
}
