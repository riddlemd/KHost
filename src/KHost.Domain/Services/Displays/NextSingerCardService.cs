using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Common.Display;
using Microsoft.Extensions.Logging;

namespace KHost.Domain.Services.Displays;

/// <summary>Names who is up, on the screens, when a host presses the button, and takes the card down
/// when they press it again.</summary>
/// <remarks>Host triggered, unlike the marquee and the break music card, which follow state. The card
/// is handed to every display as a request, since drawing it is the display's business; what is kept
/// here is only whether it is up, so the console can offer to take it down.</remarks>
public sealed class NextSingerCardService : BaseService, INextSingerCardService, IDisposable
{
    private readonly IMessageBroker _broker;
    private readonly IVenuesService _venuesService;
    private readonly ISingerQueueService _singerQueue;
    private readonly IPerformanceService _performances;
    private readonly IMediaService _media;
    private readonly IPlaybackService _playback;
    private readonly SubscriptionSet _subscriptions = new();
    private readonly Lock _gate = new();

    private bool _isAnnounced;

    // The screen drops the card when the picture is redrawn, which it is whenever the program moves.
    private PlaybackProgram? _announcedOver;

    public NextSingerCardService(
        ILogger<NextSingerCardService> logger,
        IMessageBroker broker,
        IVenuesService venuesService,
        ISingerQueueService singerQueue,
        IPerformanceService performances,
        IMediaService media,
        IPlaybackService playback)
        : base(logger)
    {
        _broker = broker;
        _venuesService = venuesService;
        _singerQueue = singerQueue;
        _performances = performances;
        _media = media;
        _playback = playback;

        // A pause or a seek is PlaybackChanged too, and leaves the picture, and so the card, alone.
        _subscriptions.Add(broker.Subscribe<PlaybackChanged>(
            _ => ClearIf(announcedOver => !Equals(_playback.CurrentProgram, announcedOver))));

        // A venue change redraws the venue's picture only while nothing is on.
        _subscriptions.Add(broker.Subscribe<SelectedVenueChanged>(
            _ => ClearIf(_ => _playback.CurrentProgram is PlaybackProgram.Idle)));
    }

    public bool IsAnnounced
    {
        get { lock (_gate) return _isAnnounced; }
    }

    public async Task<NextSingerCard?> BuildAsync(CancellationToken cancellationToken = default)
    {
        // The singer at the microphone is not up next, and a card saying so would disagree with
        // the room. The same rule the marquee applies, for the same reason.
        var singing = _playback.CurrentPerformance?.SingerId;

        if (_singerQueue.Users.FirstOrDefault(singer => singer.Id != singing) is not { } next)
            return null;

        // First by queue order: the one they are about to sing.
        var queued = await _performances.ReadQueuedAsync();
        var performance = queued.FirstOrDefault(entry => entry.SingerId == next.Id);
        var song = performance is null ? null : await _media.ReadAsync(performance.MediaId);

        var aliasesAllowed = (await _venuesService.ReadSelectedVenueAsync())?.Settings.AllowAliases ?? false;

        return new NextSingerCard
        {
            Singer = SingerNames.DisplayedFor(performance, next, aliasesAllowed),

            // A singer on the list with nothing queued is named alone. The card must not promise a
            // song that does not exist.
            Song = string.IsNullOrWhiteSpace(song?.Title) ? null : song.Title,
            Artist = string.IsNullOrWhiteSpace(song?.Artist) ? null : song.Artist,
        };
    }

    public async Task<bool> AnnounceAsync(CancellationToken cancellationToken = default)
    {
        if (await BuildAsync(cancellationToken) is not { } card)
            return false;

        // Set before the publish, so a console redrawing on the announcement reads the card as up.
        lock (_gate)
        {
            _isAnnounced = true;
            _announcedOver = _playback.CurrentProgram;
        }

        // Awaited, so the button settles once the display has been handed the card; a display
        // that fails to draw it logs and is skipped by the broker, never taking the show down.
        await _broker.PublishAsync(new NextSingerAnnounced(card), cancellationToken);
        return true;
    }

    public async Task WithdrawAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!_isAnnounced) return;

            _isAnnounced = false;
            _announcedOver = null;
        }

        await _broker.PublishAsync(new NextSingerWithdrawn(), cancellationToken);
    }

    public void Dispose() => _subscriptions.Dispose();

    /// <summary>Marks the card down when the screen has already drawn over it, and says so.</summary>
    private void ClearIf(Func<PlaybackProgram?, bool> drawnOver)
    {
        lock (_gate)
        {
            if (!_isAnnounced || !drawnOver(_announcedOver)) return;

            _isAnnounced = false;
            _announcedOver = null;
        }

        // Announced, not awaited: this runs inside another message's delivery.
        _broker.Announce(new NextSingerWithdrawn());
    }
}
