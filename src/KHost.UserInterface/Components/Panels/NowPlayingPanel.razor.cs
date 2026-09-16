using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
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
    [Inject] private IVenuesService? VenuesService { get; set; }
    [Inject] private IMessageBroker Broker { get; set; } = default!;

    /// <summary>
    /// The venue's say on whether a name queued alongside a song is honoured. Read on the way in
    /// and on SelectedVenueChanged, which is the message that fires both when the console changes
    /// venue and when the one it is running is edited.
    /// </summary>
    private bool _allowAliases;

    private readonly SubscriptionSet _subscriptions = new();

    private ElementReference _trackRef;
    private IJSObjectReference? _seekBar;

    protected override async Task OnInitializedAsync()
    {
        await ReadVenueAsync();

        // SelectedVenueChanged, not VenuesChanged: this only cares when the console is running a
        // different venue or the one it is running was edited, not when some other venue moved.
        _subscriptions.Add(Broker.Subscribe<SelectedVenueChanged>(async _ =>
        {
            await ReadVenueAsync();
            await InvokeAsync(StateHasChanged);
        }));

        if (PlaybackService is null) return;

        _subscriptions.Add(Broker.Subscribe<PlaybackChanged>(_ => OnStateChanged(null, EventArgs.Empty)));

        // The only panel that takes the position clock: it draws the playhead, and a redraw is all
        // it does with either event.
        PlaybackService.PositionChanged += OnStateChanged;
    }

    private async Task ReadVenueAsync()
        => _allowAliases = VenuesService is null
            || (await VenuesService.ReadSelectedVenueAsync())?.Settings.AllowAliases == true;

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

    public void Dispose()
    {
        _subscriptions.Dispose();

        if (PlaybackService is null) return;

        PlaybackService.PositionChanged -= OnStateChanged;
    }
}
