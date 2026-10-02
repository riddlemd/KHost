using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using KHost.Abstractions.Exceptions;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using KHost.Common.Lyrics;
using KHost.Common.Media;
using KHost.UserInterface.Services;

namespace KHost.UserInterface.Components.Panels;

public partial class NowPlayingPanel : IDisposable
{
    [Inject] private IPlaybackService PlaybackService { get; set; } = default!;
    [Inject] private ISingerQueueService SingerQueueService { get; set; } = default!;
    [Inject] private IPerformanceService PerformanceService { get; set; } = default!;
    [Inject] private IMediaService MediaService { get; set; } = default!;
    [Inject] private IDialogService DialogService { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private IMessageBroker Broker { get; set; } = default!;
    [Inject] private INextSingerCardService NextSingerCard { get; set; } = default!;
    [Inject] private IFlashService Flash { get; set; } = default!;
    [Inject] private ITimedLyricsService LyricsService { get; set; } = default!;

    private readonly SubscriptionSet _subscriptions = new();

    /// <summary>Whether there is anybody to name. Read off the queue rather than asked of the
    /// card service on every render, which would reach the database to paint a button.</summary>
    private bool _canAnnounce => SingerQueueService.Users.Count > 0;

    private ElementReference _trackRef;
    private IJSObjectReference? _seekBar;

    // Read once per song and kept: the playhead redraws twice a second, and asking the provider
    // on each of those would reopen the song's container every time.
    private Guid? _lanesMediaId;
    private IReadOnlyList<LyricLane> _lanes = [];
    private LyricLane? _oneLane;

    // The rotation's own head, never the console's selection: Ctrl/Cmd+Enter and the button beside
    // Announce both mean "whoever is up next", regardless of who a host has clicked on.
    private KHostUser? _topSinger;
    private Performance? _playNextPerformance;
    private Media? _playNextMedia;

    private bool _canPlayNext => _playNextMedia?.Status == MediaStatus.Ready;

    private string PlayNextLabel => _topSinger is { } singer ? $"Play {singer.Name}'s song" : "Play next";

    private string PlayNextTitle => (_topSinger is null
            ? "Nobody is queued to play"
            : _playNextMedia is null
                ? $"{_topSinger.Name} has no songs queued"
                : _playNextMedia.Status == MediaStatus.Broken
                    ? "This song is marked broken and can't be played"
                    : _playNextMedia.Status.IsAcquiring()
                        ? "This song is still downloading"
                        : _playNextMedia.Status != MediaStatus.Ready
                            ? "This song isn't ready to play"
                            : $"Play {_topSinger.Name}'s next song")
        + " (Ctrl+Enter)";

    protected override void OnInitialized()
    {
        _subscriptions.Add(Broker.Subscribe<PlaybackChanged>(changed =>
        {
            _ = InvokeAsync(RefreshLanesAsync);
            OnStateChanged(null, EventArgs.Empty);
        }));

        // The lanes take the words' own colours, which the adjustments can move mid-song.
        _subscriptions.Add(Broker.Subscribe<TimedLyricsSettingsChanged>(_ => InvokeAsync(() => RefreshLanesAsync(reread: true))));

        // Any of these can change who is up next or what their top song looks like.
        _subscriptions.Add(Broker.Subscribe<SingerQueueChanged>(_ => InvokeAsync(RefreshPlayNextAsync)));
        _subscriptions.Add(Broker.Subscribe<PerformancesChanged>(_ => InvokeAsync(RefreshPlayNextAsync)));
        _subscriptions.Add(Broker.Subscribe<MediaLibraryChanged>(_ => InvokeAsync(RefreshPlayNextAsync)));

        // The only panel that takes the position clock: it draws the playhead, and a redraw is all
        // it does with either event.
        PlaybackService.PositionChanged += OnStateChanged;

        _ = InvokeAsync(RefreshLanesAsync);
        _ = InvokeAsync(RefreshPlayNextAsync);
    }

    /// <summary>Same load-then-play path the queue's own ▶ button uses
    /// (<see cref="Panels.SelectedSingerInfoPanel"/>'s <c>LoadAndPlayAsync</c>): do nothing while a
    /// song is loaded, and nothing when the top singer's next song is not Ready.</summary>
    private async Task PlayNextAsync()
    {
        if (PlaybackService.CurrentPerformance is not null) return;
        if (_playNextPerformance is not { } performance || _playNextMedia is not { } media) return;
        if (media.Status != MediaStatus.Ready) return;

        if (!await PlaybackService.HasConnectedScreenAsync())
        {
            await DialogService.ShowNoScreensAsync();
            return;
        }

        try
        {
            await PlaybackService.LoadAsync(performance, media);
            await PlaybackService.PlayAsync();
        }
        catch (KHostException ex)
        {
            await DialogService.ShowErrorAsync(
                ex,
                title: "Couldn't start this song",
                onRetry: () => _ = PlayNextAsync());
        }
    }

    private async Task RefreshPlayNextAsync()
    {
        _topSinger = SingerQueueService.Users.FirstOrDefault();
        _playNextPerformance = _topSinger is null ? null : await PerformanceService.ReadSingersNextPerformanceAsync(_topSinger.Id);
        _playNextMedia = _playNextPerformance is null ? null : await MediaService.ReadAsync(_playNextPerformance.MediaId);

        StateHasChanged();
    }

    private async Task PlayAsync()
    {
        if (!await PlaybackService.HasConnectedScreenAsync())
        {
            await DialogService.ShowNoScreensAsync();
            return;
        }

        await PlaybackService.PlayAsync();
    }

    /// <summary>Puts who is up on the screens. The card replaces the venue's picture and stands
    /// until the next thing is drawn, so nothing here has to take it down again.</summary>
    private async Task AnnounceNextSingerAsync()
    {
        if (!await NextSingerCard.AnnounceAsync())
            Flash.Show("Nobody is queued to announce.", FlashType.Warning);
    }

    private async Task SeekToClickAsync(MouseEventArgs e)
    {
        if (PlaybackService.CurrentMedia?.Duration is not { } duration)
            return;

        _seekBar ??= await JS.InvokeAsync<IJSObjectReference>("import", "/js/seek-bar.js");

        var fraction = await _seekBar.InvokeAsync<double>("fractionFromClick", _trackRef, e.ClientX);

        await PlaybackService.SeekAsync(duration * fraction);
    }

    /// <summary>Works out who sings where when the song changes, or when its words are adjusted.</summary>
    private Task RefreshLanesAsync() => RefreshLanesAsync(reread: false);

    private async Task RefreshLanesAsync(bool reread)
    {
        var media = PlaybackService.CurrentMedia;
        var sameSong = media?.Id == _lanesMediaId;
        if (sameSong && !reread) return;

        // A re-read keeps the old lanes up until the new ones are in, rather than blinking them out.
        if (!sameSong)
        {
            _lanesMediaId = media?.Id;
            _lanes = [];
            _oneLane = null;
        }

        if (media is null) return;

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

        PlaybackService.PositionChanged -= OnStateChanged;
    }
}
