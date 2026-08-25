using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Plugins.Sdk.Messaging;
using KHost.Plugins.Sdk.Messaging.Messages;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Panels;

public partial class BreakMusicBar : IDisposable
{
    [Inject] private IBreakMusicService BreakMusic { get; set; } = default!;
    [Inject] private IAdService Ads { get; set; } = default!;
    [Inject] private IPlaybackService Playback { get; set; } = default!;
    [Inject] private IFlashService Flash { get; set; } = default!;
    [Inject] private IMessageBroker Broker { get; set; } = default!;

    private readonly SubscriptionSet _subscriptions = new();

    protected override void OnInitialized()
    {
        _subscriptions.Add(Broker.Subscribe<BreakMusicChanged>(_ => OnStateChanged()));
        _subscriptions.Add(Broker.Subscribe<AdsChanged>(_ => OnStateChanged()));
    }

    public void Dispose()
    {
        _subscriptions.Dispose();

        GC.SuppressFinalize(this);
    }

    private async void OnStateChanged()
        => await InvokeAsync(StateHasChanged);

    // Only reached when the provider names no track. A provider driving another app never does,
    // so Playing has to be spelled out here or the bar reports "off" over audible music.
    private string DescribeState() => BreakMusic.State switch
    {
        BreakMusicState.Playing => $"Playing on {BreakMusic.ActiveProvider?.DisplayName ?? "break music"}",
        BreakMusicState.Paused => "Break music paused",
        BreakMusicState.Suspended => "Break music waiting",
        _ => "Break music off",
    };

    private async Task PlayAsync()
    {
        // Resume rather than restart when it was paused, or the host loses their place in the
        // playlist every time they take the room down for an announcement.
        if (BreakMusic.State == BreakMusicState.Paused)
        {
            await BreakMusic.ResumeAsync();
            return;
        }

        // Both causes named, because the service reports only that it did not start: a venue with
        // no playlist chosen and a venue with no screen attached look identical from here, and
        // blaming the wrong one sends the host to the wrong page.
        if (!await BreakMusic.StartAsync())
            Flash.Show("Break music did not start — check this venue has a playlist and a screen is connected.", FlashType.Warning);
    }

    private Task PauseAsync() => BreakMusic.PauseAsync();

    private Task SkipAsync() => BreakMusic.SkipAsync();

    private async Task PlayAdAsync()
    {
        if (await Ads.PlayNowAsync())
            return;

        // The service reports only that nothing played. A missing screen is the one cause the
        // console can tell apart for itself, and it is the one a host would otherwise go looking
        // for in the playlist — an ad has nowhere to appear before it has anything wrong with it.
        Flash.Show(
            await Playback.HasConnectedScreenAsync()
                ? "No ad played — the playlist is empty or a song is loaded."
                : "No ad played — no screen is connected.",
            FlashType.Warning);
    }
}
