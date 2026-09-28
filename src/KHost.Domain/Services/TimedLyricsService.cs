using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KHost.Domain.Services;

/// <inheritdoc cref="ITimedLyricsService"/>
/// <remarks>Also the one place <see cref="TimedLyricsSettingsChanged"/> is announced from: it owns
/// the adjustments, and hearing the options rather than the settings page catches a hand edit too.</remarks>
public sealed class TimedLyricsService : ITimedLyricsService, IStartsWithTheHost, IDisposable
{
    /// <summary>A save can surface as several reloads, the file read part-written between them,
    /// and each is a step towards one set of values rather than a change of its own.</summary>
    private static readonly TimeSpan DefaultSettle = TimeSpan.FromMilliseconds(100);

    private readonly ILogger<TimedLyricsService> _logger;
    private readonly IReadOnlyList<ITimedLyricsProvider> _providers;
    private readonly IOptionsMonitor<PlaybackService.ServiceOptions> _options;
    private readonly IMessageBroker _broker;
    private readonly IDisposable? _optionsSubscription;
    private readonly Timer _settleTimer;
    private readonly TimeSpan _settle;
    private readonly Lock _gate = new();

    // What the words were last adjusted by, so a save that moved something else says nothing.
    private Adjustments _announced;

    public TimedLyricsService(
        ILogger<TimedLyricsService> logger,
        IEnumerable<ITimedLyricsProvider> providers,
        IOptionsMonitor<PlaybackService.ServiceOptions> options,
        IMessageBroker broker,
        TimeSpan? settle = null)
    {
        _logger = logger;
        _providers = [.. providers];
        _options = options;
        _broker = broker;
        _settle = settle ?? DefaultSettle;
        _announced = Adjustments.Of(options.CurrentValue);
        _settleTimer = new Timer(_ => AnnounceIfMoved(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _optionsSubscription = options.OnChange((_, _) => _settleTimer.Change(_settle, Timeout.InfiniteTimeSpan));
    }

    public async Task<TimedLyrics?> GetTimedLyricsAsync(string filePath, CancellationToken cancellationToken = default)
    {
        foreach (var provider in _providers)
        {
            bool claimed;

            // A provider that throws deciding is skipped rather than fatal: the next one may own
            // the file, and a song must still load when a plugin is having a bad day.
            try { claimed = provider.CanProvide(filePath); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "A lyric provider failed deciding whether it owns '{FilePath}'", filePath);
                continue;
            }

            if (!claimed) continue;

            // Answering null once it has claimed the file ends the search: nobody else can read a
            // container its owner could not.
            TimedLyrics? lyrics;
            try { lyrics = await provider.GetTimedLyricsAsync(filePath, cancellationToken); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "The lyric provider that owns '{FilePath}' could not read it", filePath);
                return null;
            }

            return lyrics is null ? null : Adjusted(lyrics, filePath);
        }

        return null;
    }

    /// <summary>Adjusted here, the one door both the screen and the burn-in read through, so they agree.</summary>
    private TimedLyrics Adjusted(TimedLyrics lyrics, string filePath)
    {
        var settings = _options.CurrentValue;

        if (settings.ColorBlindFriendlyLyrics)
            lyrics = AdjustedOrAsIs(lyrics, ColorBlindSafeLyrics.Separate, "separating its colours", filePath);

        if (settings.DynamicLeadIns)
            lyrics = AdjustedOrAsIs(lyrics, l => LeadInGenerator.AddMissing(l, settings.DynamicLeadInPauseSeconds), "adding lead-ins", filePath);

        return lyrics;
    }

    // A failed adjustment costs only itself: a song must never lose its words over an optional nicety.
    private TimedLyrics AdjustedOrAsIs(TimedLyrics lyrics, Func<TimedLyrics, TimedLyrics> adjust, string what, string filePath)
    {
        try { return adjust(lyrics); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Showing the lyrics of '{FilePath}' as supplied: {What} failed", filePath, what);
            return lyrics;
        }
    }

    private void AnnounceIfMoved()
    {
        var now = Adjustments.Of(_options.CurrentValue);

        lock (_gate)
        {
            if (now == _announced) return;
            _announced = now;
        }

        _broker.Announce(new TimedLyricsSettingsChanged());
    }

    public void Dispose()
    {
        _optionsSubscription?.Dispose();
        _settleTimer.Dispose();
    }

    /// <summary>Everything <see cref="Adjusted"/> reads, compared as one value.</summary>
    private readonly record struct Adjustments(bool ColorBlind, bool LeadIns, int LeadInPauseSeconds)
    {
        // The pause only matters while lead-ins are on; moving it with them off changes no word.
        public static Adjustments Of(PlaybackService.ServiceOptions options) => new(
            options.ColorBlindFriendlyLyrics,
            options.DynamicLeadIns,
            options.DynamicLeadIns ? options.DynamicLeadInPauseSeconds : 0);
    }
}
