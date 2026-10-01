using KHost.Abstractions.Exceptions;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
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

    private void OnStateChanged()
        => _ = InvokeAsync(StateHasChanged);

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
        try
        {
            // Resume rather than restart when it was paused, or the host loses their place in the
            // playlist every time they take the room down for an announcement.
            if (BreakMusic.State == BreakMusicState.Paused)
            {
                await BreakMusic.ResumeAsync();

                // Resume reports nothing, so the refusal is read off the state it did not reach.
                if (BreakMusic.State != BreakMusicState.Playing)
                    WarnItDidNotStart(reason: null);

                return;
            }

            if (!await BreakMusic.StartAsync())
                WarnItDidNotStart(reason: null);
        }
        catch (KHostException ex)
        {
            // The one way a provider can tell the host why, same channel every other extension
            // point reports a failure through; a plugin stuck logging it locally otherwise reads
            // as the library provider's own playlist/screen advice, which may not even apply.
            WarnItDidNotStart(ex.WhatHappened);
        }
    }

    /// <summary>A loaded song is the only cause this can name without the provider's own reason;
    /// the rest look identical here.</summary>
    private void WarnItDidNotStart(string? reason)
    {
        var message = Playback.CurrentPerformance is not null
            ? "Break music did not start: a song is loaded. It comes back on its own after the song."
            : reason is not null
                ? $"Break music did not start: {reason}"
                : ReferenceEquals(BreakMusic.ActiveProvider, BreakMusic.LibraryProvider)
                    ? "Break music did not start: check this venue has a playlist and a screen is connected."
                    : $"Break music did not start ({BreakMusic.ActiveProvider?.DisplayName ?? "no provider"}).";

        Flash.Show(message, FlashType.Warning);
    }

    private Task PauseAsync() => BreakMusic.PauseAsync();

    private async Task SkipAsync()
    {
        var before = BreakMusic.CurrentTrack;

        await BreakMusic.SkipAsync();

        // Skipping is refused over a singer too, and silently doing nothing reads as a dead button.
        if (Playback.CurrentPerformance is not null && ReferenceEquals(before, BreakMusic.CurrentTrack))
            Flash.Show("Break music did not skip: a song is loaded.", FlashType.Warning);
    }

    private async Task PlayAdAsync()
    {
        if (await Ads.PlayNowAsync())
            return;

        // The service reports only that nothing played. A missing screen is the one cause
        // distinguishable here; an ad has nowhere to appear before anything else is wrong.
        Flash.Show(
            await Playback.HasConnectedScreenAsync()
                ? "No ad played: the playlist is empty or a song is loaded."
                : "No ad played: no screen is connected.",
            FlashType.Warning);
    }
}
