using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KHost.Domain.Services;

/// <summary>Who sings next, and the one place <see cref="UpNextChanged"/> is announced from.</summary>
/// <remarks>Hears everything that can move the list and says so once: the queue, the performances,
/// who is at the mic, and the venue's alias rule. The producers stay unaware of it.</remarks>
public sealed class UpNextService : BaseService, IUpNextService, IStartsWithTheHost, IDisposable
{
    /// <summary>One host action announces from several services in turn — a stop is the playback,
    /// the dequeue and the rotation — and each is a step towards one list, not a list of its own.</summary>
    private static readonly TimeSpan DefaultSettle = TimeSpan.FromMilliseconds(50);

    private readonly IMessageBroker _broker;
    private readonly IVenuesService _venuesService;
    private readonly ISingerQueueService _singerQueue;
    private readonly IPerformanceService _performances;
    private readonly IMediaService _media;
    private readonly SubscriptionSet _subscriptions = new();

    // Resolved on use: playback takes every display provider, and a plugin's display taking this
    // service in its constructor would otherwise close the ring.
    private readonly IServiceProvider _services;

    private readonly TimeSpan _settle;
    private readonly Lock _gate = new();
    private bool _owed;
    private bool _draining;

    // What the list last depended on, so a pause, a seek or an unrelated venue edit says nothing.
    private Guid? _singing;
    private bool? _aliasesAllowed;

    private IPlaybackService? Playback => _services.GetService<IPlaybackService>();

    public UpNextService(
        ILogger<UpNextService> logger,
        IMessageBroker broker,
        IVenuesService venuesService,
        ISingerQueueService singerQueue,
        IPerformanceService performances,
        IMediaService media,
        IServiceProvider services,
        TimeSpan? settle = null)
        : base(logger)
    {
        _broker = broker;
        _venuesService = venuesService;
        _singerQueue = singerQueue;
        _performances = performances;
        _media = media;
        _services = services;
        _settle = settle ?? DefaultSettle;

        _subscriptions.Add(broker.Subscribe<SingerQueueChanged>(_ => Owe()));
        _subscriptions.Add(broker.Subscribe<PerformancesChanged>(_ => Owe()));
        _subscriptions.Add(broker.Subscribe<PlaybackChanged>(_ => OnPlaybackChanged()));
        _subscriptions.Add(broker.Subscribe<SelectedVenueChanged>((_, _) => OnSelectedVenueChangedAsync()));
    }

    public async Task<IReadOnlyList<UpNextEntry>> ReadAsync(int count, CancellationToken cancellationToken = default)
    {
        // The singer holding the mic is not up next. Dropped before the count is taken, so a venue
        // asking for three names still gets three.
        var singing = Playback?.CurrentPerformance?.SingerId;

        var singers = _singerQueue.Users
            .Where(singer => singer.Id != singing)
            .Take(count)
            .ToList();

        if (singers.Count == 0)
            return [];

        var aliasesAllowed = (await _venuesService.ReadSelectedVenueAsync())?.Settings.AllowAliases ?? false;
        var queued = await _performances.ReadQueuedAsync();
        var entries = new List<UpNextEntry>(singers.Count);

        for (var index = 0; index < singers.Count; index++)
        {
            var singer = singers[index];

            // First by queue order, which is the one they are about to sing.
            var next = queued.FirstOrDefault(performance => performance.SingerId == singer.Id);
            var song = next is null ? null : await _media.ReadAsync(next.MediaId);
            var title = Blank(song?.Title);

            entries.Add(new UpNextEntry
            {
                Position = index + 1,
                Singer = NameFor(next, singer, aliasesAllowed),

                // A row with no title is no song worth promising; its artist goes with it.
                Title = title,
                Artist = title is null ? null : Blank(song?.Artist),
            });
        }

        return entries;
    }

    public void Dispose() => _subscriptions.Dispose();

    /// <summary>The name recorded at queue time, unless the venue prefers the singer it knows.</summary>
    /// <remarks>Off the performance, not the account: a song-first remote lets a guest type a name
    /// per pick. Every name here belongs to a turn not yet started.</remarks>
    private static string NameFor(Performance? next, KHostUser singer, bool aliasesAllowed)
    {
        var recorded = next?.SungAs?.Trim();

        return string.IsNullOrEmpty(recorded) || !aliasesAllowed ? singer.Name : recorded;
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private void OnPlaybackChanged()
    {
        var singing = Playback?.CurrentPerformance?.SingerId;

        lock (_gate)
        {
            if (singing == _singing) return;
            _singing = singing;
        }

        Owe();
    }

    private async Task OnSelectedVenueChangedAsync()
    {
        bool aliasesAllowed;

        try
        {
            aliasesAllowed = (await _venuesService.ReadSelectedVenueAsync())?.Settings.AllowAliases ?? false;
        }
        catch (Exception ex)
        {
            // Unknown is announced: a list that may have moved is worth a re-read.
            Logger.LogWarning(ex, "Could not read the venue's alias rule");
            Owe();
            return;
        }

        lock (_gate)
        {
            if (aliasesAllowed == _aliasesAllowed) return;
            _aliasesAllowed = aliasesAllowed;
        }

        Owe();
    }

    /// <summary>Owed announcements pile up for the settle window and go out as one.</summary>
    private void Owe()
    {
        lock (_gate)
        {
            _owed = true;

            if (_draining) return;
            _draining = true;
        }

        _ = Task.Run(DrainAsync);
    }

    private async Task DrainAsync()
    {
        while (true)
        {
            // Waited out before every announcement, including one owed while the last went out: the
            // window has to start at a burst's first step or it splits the burst.
            await Task.Delay(_settle);

            lock (_gate)
            {
                _owed = false;
            }

            _broker.Announce(new UpNextChanged());

            lock (_gate)
            {
                if (!_owed)
                {
                    _draining = false;
                    return;
                }
            }
        }
    }
}
