using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Abstractions.Services.IPC;
using Microsoft.Extensions.Logging;

namespace KHost.Domain.Services;

/// <summary>Composes what the marquee should say; the display decides when to draw it.</summary>
/// <remarks>Holds no subscriptions: ScreenDisplayProvider hears what moves the marquee and asks
/// for it again, so the host keeps no idea of how or when a display shows it.</remarks>
public sealed class ScreenMarqueeService : BaseService, IScreenMarqueeService
{
    private readonly IVenuesService _venuesService;
    private readonly ISingerQueueService _singerQueue;
    private readonly IPerformanceService _performances;
    private readonly IMediaService _media;
    private readonly IPlaybackService _playback;

    public ScreenMarqueeService(
        ILogger<ScreenMarqueeService> logger,
        IVenuesService venuesService,
        ISingerQueueService singerQueue,
        IPerformanceService performances,
        IMediaService media,
        IPlaybackService playback)
        : base(logger)
    {
        _venuesService = venuesService;
        _singerQueue = singerQueue;
        _performances = performances;
        _media = media;
        _playback = playback;
    }

    /// <summary>Nothing to push: no screen can be up before the hub is mapped, and the display
    /// draws the marquee whole on every connect.</summary>
    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public async Task<SetMarqueeCommand> BuildAsync(CancellationToken cancellationToken = default)
    {
        var venue = await _venuesService.ReadSelectedVenueAsync();
        var settings = venue?.Settings;

        if (settings is null || !settings.MarqueeEnabled)
            return new SetMarqueeCommand { Enabled = false };

        return new SetMarqueeCommand
        {
            Enabled = true,
            Singers = await UpNextAsync(settings.MarqueeSingerCount, settings.MarqueeEntryFormat, settings.AllowAliases),
            Message = SingleLine(settings.MarqueeMessage),
            Position = settings.MarqueePosition,
            BackgroundColor = Blank(settings.MarqueeBackgroundColor),
            TextColor = Blank(settings.MarqueeTextColor),
            FontSizePixels = settings.MarqueeFontSizePixels,
            ScrollSpeed = settings.MarqueeScrollSpeed,
            PinLabel = settings.MarqueePinLabel,
        };
    }

    /// <summary>Composed for a venue with no wording of its own, or one that cleared it.</summary>
    private const string DefaultEntryFormat = "{song} - {singer}";

    /// <summary>One line per upcoming turn; a singer with nothing queued is named alone.</summary>
    /// <remarks>Whoever is singing now is left out of the count entirely.</remarks>
    private async Task<List<string>> UpNextAsync(int wanted, string? entryFormat, bool aliasesAllowed)
    {
        // The singer holding the mic is not up next, and the band says they are. Dropped before
        // the count is taken, so a venue asking for three names still gets three.
        var singing = _playback.CurrentPerformance?.SingerId;

        var singers = _singerQueue.Users
            .Where(singer => singer.Id != singing)
            .Take(wanted)
            .ToList();

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

            // Off the performance, not the account: a song-first remote lets a guest type a name per pick.
            // Playback only resolves the song playing; every name here belongs to a turn not yet started.
            var name = NameFor(next, singer, aliasesAllowed);

            lines.Add(string.IsNullOrWhiteSpace(media?.Title)
                ? name
                : ComposeEntry(format, media, name, index + 1));
        }

        return lines;
    }

    /// <summary>The name recorded at queue time, unless the venue prefers the singer it knows.</summary>
    private static string NameFor(Performance? next, KHostUser singer, bool aliasesAllowed)
    {
        var recorded = next?.SungAs?.Trim();

        return string.IsNullOrEmpty(recorded) || !aliasesAllowed ? singer.Name : recorded;
    }

    /// <summary>Replaces every tag a host may use; one absent from the format is simply not shown.</summary>
    private static string ComposeEntry(string format, Media media, string singer, int position)
        => format
            .Replace("{song}", media.Title.Trim(), StringComparison.OrdinalIgnoreCase)
            .Replace("{artist}", media.Artist.Trim(), StringComparison.OrdinalIgnoreCase)
            .Replace("{singer}", singer, StringComparison.OrdinalIgnoreCase)
            .Replace("{position}", position.ToString(), StringComparison.OrdinalIgnoreCase);

    /// <summary>A cleared colour is no colour, not an empty CSS value the screen would take.</summary>
    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Collapses a message to one line so the stored version is never rewritten.</summary>
    private static string? SingleLine(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
