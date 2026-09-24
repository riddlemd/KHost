using KHost.Abstractions.Messaging;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Abstractions.Services.IPC;
using Microsoft.Extensions.Logging;

namespace KHost.Domain.Services.Screens;

/// <summary>Names who is up, on the screens, when a host presses the button.</summary>
/// <remarks>Host triggered and one shot, unlike the marquee and the break music card, which follow
/// state. Nothing subscribes and nothing republishes: the card stands until the next thing drawn.
/// It is handed to the display as a request, since drawing it is the display's business.</remarks>
public sealed class NextSingerCardService(
    ILogger<NextSingerCardService> logger,
    IMessageBroker broker,
    IVenuesService venuesService,
    ISingerQueueService singerQueue,
    IPerformanceService performances,
    IMediaService media,
    IPlaybackService playback) : BaseService(logger), INextSingerCardService
{
    public async Task<ShowNextSingerCommand?> BuildAsync(CancellationToken cancellationToken = default)
    {
        // The singer at the microphone is not up next, and a card saying so would disagree with
        // the room. The same rule the marquee applies, for the same reason.
        var singing = playback.CurrentPerformance?.SingerId;

        if (singerQueue.Users.FirstOrDefault(singer => singer.Id != singing) is not { } next)
            return null;

        // First by queue order: the one they are about to sing.
        var queued = await performances.ReadQueuedAsync();
        var performance = queued.FirstOrDefault(entry => entry.SingerId == next.Id);
        var song = performance is null ? null : await media.ReadAsync(performance.MediaId);

        var aliasesAllowed = (await venuesService.ReadSelectedVenueAsync())?.Settings.AllowAliases ?? false;

        return new ShowNextSingerCommand
        {
            Singer = NameFor(performance, next, aliasesAllowed),

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

        // Awaited, so the button settles once the display has been handed the card; a display
        // that fails to draw it logs and is skipped by the broker, never taking the show down.
        await broker.PublishAsync(new NextSingerCardRequested(card), cancellationToken);
        return true;
    }

    /// <summary>The name recorded at queue time, unless the venue prefers the singer it knows. Off
    /// the performance rather than the account, since a remote lets a guest type a name per pick.
    /// </summary>
    private static string NameFor(Performance? performance, KHostUser singer, bool aliasesAllowed)
    {
        var recorded = performance?.SungAs?.Trim();

        return string.IsNullOrEmpty(recorded) || !aliasesAllowed ? singer.Name : recorded;
    }
}
