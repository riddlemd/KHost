using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using KHost.Common.Lyrics;
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
    [Inject] private ITimedLyricsService? LyricsService { get; set; }

    private readonly SubscriptionSet _subscriptions = new();

    /// <summary>Whether there is anybody to name. Read off the queue rather than asked of the
    /// card service on every render, which would reach the database to paint a button.</summary>
    private bool _canAnnounce => SingerQueueService?.Users.Count > 0;

    private ElementReference _trackRef;
    private IJSObjectReference? _seekBar;

    // Read once per song and kept: the playhead redraws twice a second, and asking the provider
    // on each of those would reopen the song's container every time.
    private Guid? _lanesMediaId;
    private IReadOnlyList<LyricLane> _lanes = [];
    private LyricLane? _oneLane;

    protected override void OnInitialized()
    {
        if (PlaybackService is null) return;

        _subscriptions.Add(Broker.Subscribe<PlaybackChanged>(changed =>
        {
            _ = InvokeAsync(RefreshLanesAsync);
            OnStateChanged(null, EventArgs.Empty);
        }));

        // The only panel that takes the position clock: it draws the playhead, and a redraw is all
        // it does with either event.
        PlaybackService.PositionChanged += OnStateChanged;

        _ = InvokeAsync(RefreshLanesAsync);
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

    /// <summary>Works out who sings where when the song changes, and at no other time.</summary>
    private async Task RefreshLanesAsync()
    {
        var media = PlaybackService?.CurrentMedia;
        if (media?.Id == _lanesMediaId) return;

        _lanesMediaId = media?.Id;
        _lanes = [];
        _oneLane = null;

        if (media is null || LyricsService is null) return;

        TimedLyrics? lyrics;
        try { lyrics = await LyricsService.GetTimedLyricsAsync(media.FilePath); }
        // Nothing awaits this; a failure only costs the lanes, and the plain bar still seeks.
        catch (Exception) { return; }

        // A newer song may have started while this one was being read.
        if (lyrics is null || _lanesMediaId != media.Id) return;

        _lanes = LyricLanes.SungSpansByVoice(lyrics);
        _oneLane = LyricLanes.SungSpansAsOneLane(lyrics);
        StateHasChanged();
    }

    /// <summary>A position as a percentage of the song, for an SVG coordinate.</summary>
    private static string PercentOf(double seconds, double durationSeconds)
        => Percent(Math.Clamp(seconds / durationSeconds * 100, 0, 100));

    /// <summary>Where lane <paramref name="index"/> of <paramref name="count"/> sits in the track,
    /// as SVG percentages. A lone lane is half the track's height; stacked
    /// lanes take more of their band so each stays thick enough to see.</summary>
    private static (string Y, string Height) LaneBand(int index, int count)
    {
        var band = 100.0 / count;
        var height = band * (count == 1 ? 0.5 : 0.7);
        var y = band * index + (band - height) / 2;

        return (Percent(y), Percent(height));
    }

    private static string Percent(double value)
        => value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "%";

    private static string? FillOf(LyricLane lane)
        => lane.Color is { } c ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : null;

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
