using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Abstractions.Services.IPC;
using Microsoft.Extensions.Logging;

namespace KHost.Domain.Services;

/// <summary>Composes what the marquee should say; the display decides when to draw it.</summary>
/// <remarks>Holds no subscriptions: a display hears what moves the marquee and asks for it again, so
/// the host keeps no idea of how or when a display shows it. The singers come from
/// <see cref="IUpNextService"/>, so the band and anything else naming who is next cannot disagree.</remarks>
public sealed class ScreenMarqueeService : BaseService, IScreenMarqueeService
{
    private readonly IVenuesService _venuesService;
    private readonly IUpNextService _upNext;

    public ScreenMarqueeService(
        ILogger<ScreenMarqueeService> logger,
        IVenuesService venuesService,
        IUpNextService upNext)
        : base(logger)
    {
        _venuesService = venuesService;
        _upNext = upNext;
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

    /// <summary>Composed for a venue with no wording of its own, or one that cleared it.</summary>
    private const string DefaultEntryFormat = "{song} - {singer}";

    /// <summary>One line per upcoming turn; a singer with nothing queued is named alone.</summary>
    private async Task<List<string>> UpNextAsync(int wanted, string? entryFormat)
    {
        var entries = await _upNext.ReadAsync(wanted);

        if (entries.Count == 0)
            return [];

        var format = string.IsNullOrWhiteSpace(entryFormat) ? DefaultEntryFormat : entryFormat;

        return [.. entries.Select(entry => entry.Title is null ? entry.Singer : ComposeEntry(format, entry))];
    }

    /// <summary>Replaces every tag a host may use; one absent from the format is simply not shown.</summary>
    private static string ComposeEntry(string format, UpNextEntry entry)
        => format
            .Replace("{song}", entry.Title, StringComparison.OrdinalIgnoreCase)
            .Replace("{artist}", entry.Artist ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{singer}", entry.Singer, StringComparison.OrdinalIgnoreCase)
            .Replace("{position}", entry.Position.ToString(), StringComparison.OrdinalIgnoreCase);

    /// <summary>A cleared colour is no colour, not an empty CSS value the screen would take.</summary>
    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Collapses a message to one line so the stored version is never rewritten.</summary>
    private static string? SingleLine(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
