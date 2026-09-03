using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Abstractions.Services.IPC;
using Microsoft.Extensions.Logging;

namespace KHost.Domain.Services;

/// <summary>
/// Keeps the screens' marquee saying what the room should see. Apart from
/// <see cref="ScreenCoordinationService"/>, which decides which screen is heard rather than what
/// any of them shows, and apart from playback, which owns the picture and has no reason to know
/// the queue's order.
/// </summary>
public sealed class ScreenMarqueeService : BaseService, IScreenMarqueeService, IDisposable
{
    private readonly IScreenServer _screenServer;
    private readonly IVenuesService _venuesService;
    private readonly ISingerQueueService _singerQueue;
    private readonly IPerformanceService _performances;
    private readonly IMediaService _media;
    private readonly SubscriptionSet _subscriptions = new();

    public ScreenMarqueeService(
        ILogger<ScreenMarqueeService> logger,
        IScreenServer screenServer,
        IVenuesService venuesService,
        ISingerQueueService singerQueue,
        IPerformanceService performances,
        IMediaService media,
        IMessageBroker broker)
        : base(logger)
    {
        _screenServer = screenServer;
        _venuesService = venuesService;
        _singerQueue = singerQueue;
        _performances = performances;
        _media = media;

        // The queue's order is the marquee's content, and the venue owns everything about how it
        // looks — including whether there is one at all.
        _subscriptions.Add(broker.Subscribe<SingerQueueChanged>(_ => Republish()));
        _subscriptions.Add(broker.Subscribe<PerformancesChanged>(_ => Republish()));
        _subscriptions.Add(broker.Subscribe<SelectedVenueChanged>(_ => Republish()));

        // A screen that joins mid-show has never been sent one.
        _screenServer.ScreenConnected += OnScreenConnected;
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
        => BroadcastAsync(cancellationToken);

    public async Task<SetMarqueeCommand> BuildAsync(CancellationToken cancellationToken = default)
    {
        var venue = await _venuesService.ReadSelectedVenueAsync();
        var settings = venue?.Settings;

        if (settings is null || !settings.MarqueeEnabled)
            return new SetMarqueeCommand { Enabled = false };

        return new SetMarqueeCommand
        {
            Enabled = true,
            Singers = await UpNextAsync(settings.MarqueeSingerCount, settings.MarqueeEntryFormat),
            Message = SingleLine(settings.MarqueeMessage),
            Position = settings.MarqueePosition,
            BackgroundColor = Blank(settings.MarqueeBackgroundColor),
            TextColor = Blank(settings.MarqueeTextColor),
            FontSizePixels = settings.MarqueeFontSizePixels,
            ScrollSpeed = settings.MarqueeScrollSpeed,
            PinLabel = settings.MarqueePinLabel,
        };
    }

    /// <summary>Composed for a venue that has never chosen its own wording, and for one that cleared it.</summary>
    private const string DefaultEntryFormat = "{song} - {singer}";

    /// <summary>
    /// One line per upcoming turn: the song and who is singing it, shaped by the venue's own
    /// format. A singer with nothing queued is still up next — the host has them on the list — so
    /// they are named on their own rather than dropped, which would make the band disagree with
    /// the queue on screen.
    /// </summary>
    private async Task<List<string>> UpNextAsync(int wanted, string? entryFormat)
    {
        var singers = _singerQueue.Users.Take(wanted).ToList();

        if (singers.Count == 0)
            return [];

        var format = string.IsNullOrWhiteSpace(entryFormat) ? DefaultEntryFormat : entryFormat;
        var queued = await _performances.ReadQueuedAsync();
        var lines = new List<string>(singers.Count);

        for (var index = 0; index < singers.Count; index++)
        {
            var singer = singers[index];

            // First by queue order, which is the one they are about to sing.
            var next = queued.FirstOrDefault(performance => performance.SingerId == singer.Id);
            var media = next is null ? null : await _media.ReadAsync(next.MediaId);

            lines.Add(string.IsNullOrWhiteSpace(media?.Title)
                ? singer.Name
                : ComposeEntry(format, media, singer, index + 1));
        }

        return lines;
    }

    /// <summary>Replaces every tag a host may use; one not present in the format is simply not shown.</summary>
    private static string ComposeEntry(string format, Media media, KHostUser singer, int position)
        => format
            .Replace("{song}", media.Title.Trim(), StringComparison.OrdinalIgnoreCase)
            .Replace("{artist}", media.Artist.Trim(), StringComparison.OrdinalIgnoreCase)
            .Replace("{singer}", singer.Name, StringComparison.OrdinalIgnoreCase)
            .Replace("{position}", position.ToString(), StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        _screenServer.ScreenConnected -= OnScreenConnected;
        _subscriptions.Dispose();
    }

    private async Task BroadcastAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _screenServer.BroadcastCommandAsync(await BuildAsync(cancellationToken));
        }
        catch (Exception ex)
        {
            // A marquee that fails to reach the screens must not take the show down with it.
            Logger.LogWarning(ex, "Failed to send the marquee to screens");
        }
    }

    // ScreenConnected arrives on the hub thread already holding a lock, so nothing here may be
    // awaited on it.
    private void OnScreenConnected(object? sender, ScreenConnectionEventArgs e) => _ = Task.Run(async () =>
    {
        try
        {
            await _screenServer.SendCommandAsync(e.Connection.ScreenId, await BuildAsync());
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to send the marquee to screen {ScreenId}", e.Connection.ScreenId);
        }
    });

    private void Republish() => _ = Task.Run(() => BroadcastAsync());

    /// <summary>A colour the host cleared is no colour, not an empty CSS value the screen would take.</summary>
    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// The band is one line and cannot become two, so a pasted message keeps its words and loses
    /// its shape. Done here rather than on the screen: every consumer of this command gets the
    /// same string, and a venue's stored message is not rewritten behind their back.
    /// </summary>
    private static string? SingleLine(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
