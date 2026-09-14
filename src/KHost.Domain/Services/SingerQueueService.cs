using KHost.Abstractions.Models;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using KHost.Abstractions.Models.QueueRotation;
using KHost.Abstractions.Services;
using KHost.Abstractions.Services.QueueRotation;
using Microsoft.Extensions.Logging;

namespace KHost.Domain.Services;

public class SingerQueueService : ISingerQueueService, IDisposable
{
    private const string _cacheKey = "singer-queue";

    private readonly ILogger<SingerQueueService> _logger;
    private readonly ICacheService _cacheService;
    private readonly IPerformanceService _performanceService;
    private readonly IUsersService _usersService;
    private readonly IVenuesService _venuesService;
    private readonly IAnalyticsService _analytics;
    private readonly IQueueRotationStrategyFactory _rotationStrategyFactory;
    private readonly IMessageBroker _broker;
    private readonly List<Guid> _userIds = [];
    private List<KHostUser> _cachedUsers = [];
    private readonly SubscriptionSet _subscriptions = new();


    public IReadOnlyList<KHostUser> Users => _cachedUsers.AsReadOnly();
    public Guid? SelectedUserId { get; private set; }
    public KHostUser? SelectedUser =>
        SelectedUserId is { } id ? _cachedUsers.FirstOrDefault(u => u.Id == id) : null;
    public bool IsTopSlotLocked => _isTopSlotLocked;

    private bool _isTopSlotLocked;

    public SingerQueueService(
        ILogger<SingerQueueService> logger,
        ICacheService cacheService,
        IPerformanceService performanceService,
        IUsersService usersService,
        IVenuesService venuesService,
        IAnalyticsService analytics,
        IQueueRotationStrategyFactory rotationStrategyFactory,
        IMessageBroker broker)
    {
        _logger = logger;
        _cacheService = cacheService;
        _performanceService = performanceService;
        _usersService = usersService;
        _venuesService = venuesService;
        _analytics = analytics;
        _rotationStrategyFactory = rotationStrategyFactory;
        _broker = broker;

        // A singer deleted in the users manager is still in this queue: the order is a list of
        // ids, and nothing here was told. Resolving simply skipped them, so the panel showed one
        // fewer singer than the rotation was counting — the two disagreed, quietly, and the
        // ghost's turn still came round with nobody there.
        _subscriptions.Add(broker.Subscribe<UsersChanged>(message => { _ = Task.Run(PruneDeletedSingersAsync); }));
    }

    /// <summary>
    /// Drops singers who no longer exist, and the songs they had waiting. Driven by the
    /// announcement rather than by the users manager calling in, so a singer deleted down any path
    /// leaves the same way — and so this service keeps owning its own queue.
    /// </summary>
    /// <remarks>
    /// The queued songs are deleted rather than unqueued. Nobody sang them and nobody now can, so
    /// there is no record to keep standing — which is what separates them from the performances a
    /// deletion deliberately leaves alone.
    /// </remarks>
    private async Task PruneDeletedSingersAsync()
    {
        try
        {
            List<Guid> missing = [];

            foreach (var id in _userIds.ToList())
                if (await _usersService.ReadAsync(id) is null)
                    missing.Add(id);

            if (missing.Count == 0)
                return;

            foreach (var id in missing)
            {
                _userIds.Remove(id);

                if (SelectedUserId == id)
                    SelectedUserId = null;

                var queued = await _performanceService.ReadBySingerIdAsync(id, pageSize: 0, filter: PerformanceFilter.Queued);

                foreach (var performance in queued.Items)
                    await _performanceService.DeleteAsync(performance.Id);

                _logger.LogInformation(
                    "Took deleted singer {UserId} out of the queue with {Count} song(s) waiting",
                    id, queued.Items.Count);
            }

            await NotifyAsync();
        }
        catch (Exception ex)
        {
            // A queue that fails to tidy itself must not take the announcement down with it; the
            // next user change tries again.
            _logger.LogWarning(ex, "Could not take deleted singers out of the queue");
        }
    }

    public void Dispose() => _subscriptions.Dispose();

    public async Task SelectUserAsync(Guid? userId)
    {
        SelectedUserId = userId;

        _logger.LogInformation("Selected user {UserId}", userId);

        await NotifyAsync();
    }

    public async Task AddUserAsync(Guid userId)
    {
        _userIds.Add(userId);

        _logger.LogInformation("User {UserId} added to queue", userId);

        var config = await ReadRotationConfigAsync();

        await ApplyRotationAsync(config, finishedSingerId: null, joiningSingerId: userId);

        await NotifyAsync();
    }

    public async Task RotateQueueAsync(Guid finishedSingerId)
    {
        if (_userIds.Count == 0) return;

        var config = await ReadRotationConfigAsync();

        await ApplyRotationAsync(config, finishedSingerId: finishedSingerId, joiningSingerId: null);
        await SelectFirstUserInQueueAsync();
    }

    public async Task RemoveUserAsync(Guid userId)
    {
        _userIds.Remove(userId);

        if (SelectedUserId == userId)
            SelectedUserId = null;

        _logger.LogInformation("User {UserId} removed from queue", userId);

        await NotifyAsync();
    }

    public async Task AddMediaAsync(Guid userId, MediaSearchEntity media)
    {
        if (!_userIds.Contains(userId)) return;

        // ForeignKey is whatever the provider calls the result — a library id for a local one, a
        // video id or a URL for a remote one. Only the first is a media row that already exists,
        // and importing the others is the provider's job, not the queue's.
        if (!Guid.TryParse(media.ForeignKey, out var mediaId))
        {
            _logger.LogWarning(
                "Not enqueuing {Source} result '{ForeignKey}': it is not a library media id",
                media.Source, media.ForeignKey);

            return;
        }

        await _performanceService.CreateAndEnqueueAsync(new Performance
        {
            SingerId = userId,
            MediaId = mediaId,
            CreatedDate = DateTime.UtcNow,
        });
    }

    public async Task MoveUserUpAsync(Guid userId)
    {
        var idx = _userIds.IndexOf(userId);

        SelectedUserId = userId;

        if (idx > 0 && !(idx == 1 && IsTopSlotLocked))
        {
            (_userIds[idx], _userIds[idx - 1]) = (_userIds[idx - 1], _userIds[idx]);

            _logger.LogDebug("User {UserId} moved up from position {OldIndex} to {NewIndex}", userId, idx, idx - 1);
        }

        await NotifyAsync();
    }

    public async Task MoveUserDownAsync(Guid userId)
    {
        var idx = _userIds.IndexOf(userId);

        SelectedUserId = userId;

        if (idx >= 0 && idx < _userIds.Count - 1)
        {
            (_userIds[idx], _userIds[idx + 1]) = (_userIds[idx + 1], _userIds[idx]);

            _logger.LogDebug("User {UserId} moved down from position {OldIndex} to {NewIndex}", userId, idx, idx + 1);
        }

        await NotifyAsync();
    }

    public async Task MoveUserToStartAsync(Guid userId)
    {
        var idx = _userIds.IndexOf(userId);

        if (idx > 0 && !IsTopSlotLocked)
        {
            _userIds.RemoveAt(idx);

            _userIds.Insert(0, userId);

            _logger.LogDebug("User {UserId} moved to start of queue", userId);

            await NotifyAsync();
        }
    }

    public void LockTopSlot() => _isTopSlotLocked = true;

    public void UnlockTopSlot() => _isTopSlotLocked = false;

    public async Task MoveUserToEndAsync(Guid userId)
    {
        var idx = _userIds.IndexOf(userId);

        if (idx >= 0 && idx < _userIds.Count - 1)
        {
            _userIds.RemoveAt(idx);

            _userIds.Add(userId);

            _logger.LogDebug("User {UserId} moved to end of queue", userId);

            await NotifyAsync();
        }
    }

    public async Task MoveUserToIndexAsync(Guid userId, int newIndex)
    {
        var idx = _userIds.IndexOf(userId);

        if (idx < 0) return;

        if (IsTopSlotLocked && newIndex == 0) return;

        var clampedIndex = Math.Clamp(newIndex, 0, _userIds.Count - 1);

        _userIds.RemoveAt(idx);

        _userIds.Insert(clampedIndex, userId);

        _logger.LogDebug("User {UserId} moved to index {NewIndex}", userId, clampedIndex);

        await NotifyAsync();
    }

    public async Task SelectFirstUserInQueueAsync()
    {
        var firstId = _userIds.FirstOrDefault();

        if (firstId == Guid.Empty) return;

        await SelectUserAsync(firstId);
    }

    public async Task RefreshAsync()
    {
        await NotifyAsync();
    }

    public async Task ClearAsync()
    {
        var venue = await _venuesService.ReadSelectedVenueAsync();
        if (venue?.Settings.ClearQueueOnClose != true)
            return;

        _userIds.Clear();

        SelectedUserId = null;

        await SaveAsync();

        _logger.LogInformation("Singer queue cleared on close");

        await _performanceService.DeleteAllQueuedAsync();
    }

    public async Task InitializeAsync()
    {
        var queueData = await _cacheService.LoadAsync<QueueCacheData>(_cacheKey);

        if (queueData is null || queueData.UserIds.Count == 0)
        {
            _logger.LogWarning("Singer queue cache was empty or missing");
            return;
        }

        _userIds.AddRange(queueData.UserIds);
        SelectedUserId = queueData.SelectedUserId;
        await ResolveAsync();
        _logger.LogInformation("Singer queue loaded ({Count} users)", queueData.UserIds.Count);
        if (_broker is { } broker)
            _ = broker.PublishAsync(new SingerQueueChanged());
    }

    // Venues saved before rotation existed read the JSON key back as null; default to fifo,
    // whose drop-to-end matches the classic rotation those venues already had.
    private async Task<QueueRotationConfig> ReadRotationConfigAsync()
        => (await _venuesService.ReadSelectedVenueAsync())?.Settings.QueueRotation ?? new QueueRotationConfig();

    private async Task ApplyRotationAsync(QueueRotationConfig config, Guid? finishedSingerId, Guid? joiningSingerId)
    {
        try
        {
            var context = new QueueRotationContext
            {
                Queue = await BuildRotationSnapshotsAsync(config),
                FinishedSingerId = finishedSingerId,
                JoiningSingerId = joiningSingerId,
                Config = config,
                // Local midnight, expressed as UTC: a UTC midnight falls mid-show for most of the
                // Americas, and the count would reset while the room is still singing.
                SongsSungTonight = await _performanceService.CountSungSinceAsync(_userIds, DateTime.Today.ToUniversalTime()),
                Now = DateTime.UtcNow,
            };

            var newOrder = await _rotationStrategyFactory.Resolve(config).ApplyAsync(context);

            ApplyOrder(newOrder, finishedSingerId);

            _logger.LogInformation("Queue rotated with strategy '{StrategyId}'", config.StrategyId);
        }
        catch (Exception ex)
        {
            // Modes can come from plugins — a throwing strategy must not break the queue.
            _logger.LogWarning(ex, "Queue rotation failed; order left unchanged");
        }
    }

    private async Task<List<RotationSinger>> BuildRotationSnapshotsAsync(QueueRotationConfig config)
    {
        var snapshots = new List<RotationSinger>(_userIds.Count);

        foreach (var id in _userIds)
        {
            var lastPerformance = await _performanceService.ReadBySingerIdAsync(
                id, pageNumber: 1, pageSize: 1, filter: PerformanceFilter.UnQueued);

            IReadOnlyList<Guid> groupIds = [];
            if (config.VipGroupId.HasValue)
                groupIds = (await _usersService.ReadAsync(id))?.Groups.Select(g => g.Id).ToList() ?? [];

            snapshots.Add(new RotationSinger
            {
                Id = id,
                LastSangOn = lastPerformance.Items.FirstOrDefault()?.CreatedDate,
                GroupIds = groupIds,
            });
        }

        return snapshots;
    }

    // A strategy may drop the FINISHED singer (their turn ends and they leave the queue —
    // fifo's LeavesQueue drop position). Modes can come from plugins though, so anyone else
    // missing is a strategy bug and is re-appended; duplicates and unknown ids are stripped.
    private void ApplyOrder(IReadOnlyList<Guid> newOrder, Guid? finishedSingerId)
    {
        var current = new HashSet<Guid>(_userIds);
        var sanitized = newOrder.Where(current.Contains).Distinct().ToList();

        sanitized.AddRange(_userIds.Where(id => !sanitized.Contains(id) && id != finishedSingerId));

        if (finishedSingerId is { } finishedId && !sanitized.Contains(finishedId))
        {
            if (SelectedUserId == finishedId)
                SelectedUserId = null;

            _logger.LogInformation("Singer {UserId} left the queue after performing", finishedId);
        }

        _userIds.Clear();
        _userIds.AddRange(sanitized);
    }

    private async Task SaveAsync()
    {
        var queueData = new QueueCacheData
        {
            SelectedUserId = SelectedUserId,
            UserIds = _userIds
        };

        await _cacheService.SaveAsync(_cacheKey, queueData);
    }

    private async Task ResolveAsync()
    {
        var resolved = new List<KHostUser>(_userIds.Count);

        foreach (var id in _userIds)
        {
            var user = await _usersService.ReadAsync(id);
            if (user is not null)
                resolved.Add(user);
        }

        _cachedUsers = resolved;
    }

    private async Task NotifyAsync()
    {
        _analytics.RecordQueueMutation();
        await ResolveAsync();
        await SaveAsync();

        if (_broker is { } broker)
            _ = broker.PublishAsync(new SingerQueueChanged());
    }

    private class QueueCacheData
    {
        public Guid? SelectedUserId { get; set; }
        public List<Guid> UserIds { get; set; } = [];
    }
}
