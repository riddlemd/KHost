using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using KHost.UserInterface.Services;

namespace KHost.UserInterface.Components.Panels;

public partial class NowPlayingPanel : IDisposable
{
    [Inject] private IPlaybackService? PlaybackService { get; set; }
    [Inject] private ISingerQueueService? SingerQueueService { get; set; }
    [Inject] private IDialogService? DialogService { get; set; }
    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private IMessageBroker Broker { get; set; } = default!;
    [Inject] private INextSingerCardService? NextSingerCard { get; set; }
    [Inject] private IFlashService? Flash { get; set; }

    private readonly SubscriptionSet _subscriptions = new();

    /// <summary>Whether there is anybody to name. Read off the queue rather than asked of the
    /// card service on every render, which would reach the database to paint a button.</summary>
    private bool _canAnnounce => SingerQueueService?.Users.Count > 0;

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

    /// <summary>Puts who is up on the screens. The card replaces the venue's picture and stands
    /// until the next thing is drawn, so nothing here has to take it down again.</summary>
    private async Task AnnounceNextSingerAsync()
    {
        if (NextSingerCard is null)
            return;

        if (!await NextSingerCard.AnnounceAsync())
            Flash?.Show("Nobody is queued to announce.", FlashType.Warning);
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

    public void Dispose()
    {
        _subscriptions.Dispose();

        if (PlaybackService is null) return;

        PlaybackService.PositionChanged -= OnStateChanged;
    }
}
