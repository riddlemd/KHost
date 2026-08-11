using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Abstractions.Services.QueueRotation;
using Microsoft.Extensions.Logging;

namespace KHost.Domain.Services.QueueRotation;

public class QueueRotationStateService : IQueueRotationStateService
{
    private const string _cacheKey = "rotation-state";

    private readonly ILogger<QueueRotationStateService> _logger;
    private readonly ICacheService _cacheService;
    private readonly IPerformanceService _performanceService;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<Guid, int> _missedCalls = [];

    public QueueRotationStateService(
        ILogger<QueueRotationStateService> logger,
        ICacheService cacheService,
        IPerformanceService performanceService)
    {
        _logger = logger;
        _cacheService = cacheService;
        _performanceService = performanceService;
    }

    public async Task InitializeAsync()
    {
        var data = await _cacheService.LoadAsync<QueueRotationStateCacheData>(_cacheKey);

        if (data?.MissedCalls is not null)
        {
            await _gate.WaitAsync();
            try
            {
                _missedCalls.Clear();
                foreach (var (id, count) in data.MissedCalls)
                    _missedCalls[id] = count;
            }
            finally
            {
                _gate.Release();
            }
            _logger.LogInformation("Rotation state loaded ({Count} missed-call entries)", data.MissedCalls.Count);
        }
    }

    public async Task<IReadOnlyDictionary<Guid, int>> GetSongsSungTonightAsync(IEnumerable<Guid> userIds)
    {
        var startOfToday = DateTime.UtcNow.Date;
        var ids = userIds.Distinct().ToList();
        var result = new Dictionary<Guid, int>(ids.Count);

        var tasks = ids.Select(async id =>
        {
            var page = await _performanceService.ReadBySingerIdAsync(
                id,
                pageNumber: 1,
                pageSize: 0,
                filter: PerformanceFilter.UnQueued,
                startDate: startOfToday);
            return (id, page.TotalCount);
        });

        foreach (var (id, count) in await Task.WhenAll(tasks))
            result[id] = count;

        return result;
    }

    public async Task<IReadOnlyDictionary<Guid, int>> GetMissedCallsAsync()
    {
        await _gate.WaitAsync();
        try
        {
            return new Dictionary<Guid, int>(_missedCalls);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task IncrementMissedCallsAsync(Guid userId)
    {
        await _gate.WaitAsync();
        try
        {
            _missedCalls[userId] = (_missedCalls.TryGetValue(userId, out var current) ? current : 0) + 1;
        }
        finally
        {
            _gate.Release();
        }
        await SaveAsync();
        _logger.LogDebug("Missed call incremented for user {UserId}", userId);
    }

    public async Task ResetMissedCallsAsync(Guid userId)
    {
        await _gate.WaitAsync();
        try
        {
            _missedCalls.Remove(userId);
        }
        finally
        {
            _gate.Release();
        }
        await SaveAsync();
    }

    public async Task ClearAsync()
    {
        await _gate.WaitAsync();
        try
        {
            _missedCalls.Clear();
        }
        finally
        {
            _gate.Release();
        }
        await SaveAsync();
        _logger.LogInformation("Rotation state cleared");
    }

    private async Task SaveAsync()
    {
        Dictionary<Guid, int> snapshot;
        await _gate.WaitAsync();
        try
        {
            snapshot = new Dictionary<Guid, int>(_missedCalls);
        }
        finally
        {
            _gate.Release();
        }

        await _cacheService.SaveAsync(_cacheKey, new QueueRotationStateCacheData
        {
            MissedCalls = snapshot,
        });
    }

    private class QueueRotationStateCacheData
    {
        public Dictionary<Guid, int> MissedCalls { get; set; } = [];
    }
}
