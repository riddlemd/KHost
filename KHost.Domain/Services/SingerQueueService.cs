using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KHost.Domain.Services;

public class SingerQueueService : ISingerQueueService
{
    private const string _cacheKey = "singer-queue";

    private readonly ILogger<SingerQueueService> _logger;
    private readonly ICacheService _cacheService;
    private readonly IPerformanceService _performanceService;
    private readonly ISingersService _singersService;
    private readonly List<Guid> _singerIds = [];
    private List<Singer> _cachedSingers = [];

    public event EventHandler? StateChanged;

    public IReadOnlyList<Singer> Singers => _cachedSingers.AsReadOnly();
    public Guid? SelectedSingerId { get; private set; }
    public Singer? SelectedSinger =>
        SelectedSingerId is { } id ? _cachedSingers.FirstOrDefault(s => s.Id == id) : null;

    public IOptionsMonitor<ServiceOptions> Options { get; set; }

    public SingerQueueService(
        ILogger<SingerQueueService> logger,
        IOptionsMonitor<ServiceOptions> options,
        ICacheService cacheService,
        IPerformanceService performanceService,
        ISingersService singersService)
    {
        _logger = logger;
        Options = options;
        _cacheService = cacheService;
        _performanceService = performanceService;
        _singersService = singersService;

        Load();
    }

    public async Task SelectSingerAsync(Guid? singerId)
    {
        SelectedSingerId = singerId;

        _logger.LogInformation("Selected singer {SingerId}", singerId);

        await NotifyAsync();
    }

    public async Task AddSingerAsync(Guid singerId)
    {
        _singerIds.Add(singerId);

        _logger.LogInformation("Singer {SingerId} added to queue", singerId);

        await NotifyAsync();
    }

    public async Task RemoveSingerAsync(Guid singerId)
    {
        _singerIds.Remove(singerId);

        if (SelectedSingerId == singerId)
            SelectedSingerId = null;

        _logger.LogInformation("Singer {SingerId} removed from queue", singerId);

        await NotifyAsync();
    }

    public async Task AddMediaAsync(Guid singerId, MediaSearchEntity media)
    {
        if (!_singerIds.Contains(singerId)) return;

        var performance = new Performance
        {
            Id = Guid.NewGuid(),
            SingerId = singerId,
            MediaId = Guid.NewGuid()
        };
        
        await _performanceService.CreateAndEnqueueAsync(performance);
    }

    public async Task MoveSingerUpAsync(Guid singerId)
    {
        var idx = _singerIds.IndexOf(singerId);

        if (idx > 0)
        {
            (_singerIds[idx], _singerIds[idx - 1]) = (_singerIds[idx - 1], _singerIds[idx]);
            _logger.LogDebug("Singer {SingerId} moved up from position {OldIndex} to {NewIndex}", singerId, idx, idx - 1);
        }

        await NotifyAsync();
    }

    public async Task MoveSingerDownAsync(Guid singerId)
    {
        var idx = _singerIds.IndexOf(singerId);

        if (idx >= 0 && idx < _singerIds.Count - 1)
        {
            (_singerIds[idx], _singerIds[idx + 1]) = (_singerIds[idx + 1], _singerIds[idx]);
            _logger.LogDebug("Singer {SingerId} moved down from position {OldIndex} to {NewIndex}", singerId, idx, idx + 1);
        }

        await NotifyAsync();
    }

    public async Task MoveSingerToStartAsync(Guid singerId)
    {
        var idx = _singerIds.IndexOf(singerId);

        if (idx > 0)
        {
            _singerIds.RemoveAt(idx);
            _singerIds.Insert(0, singerId);
            _logger.LogDebug("Singer {SingerId} moved to start of queue", singerId);
            await NotifyAsync();
        }
    }

    public async Task MoveSingerToEndAsync(Guid singerId)
    {
        var idx = _singerIds.IndexOf(singerId);

        if (idx >= 0 && idx < _singerIds.Count - 1)
        {
            _singerIds.RemoveAt(idx);
            _singerIds.Add(singerId);
            _logger.LogDebug("Singer {SingerId} moved to end of queue", singerId);
            await NotifyAsync();
        }
    }

    public async Task SelectFirstSingerInQueueAsync()
    {
        var firstId = _singerIds.FirstOrDefault();

        if (firstId == Guid.Empty) return;
        
        await SelectSingerAsync(firstId);
    }

    public async Task RefreshAsync()
    {
        await NotifyAsync();
    }

    private async void Load()
    {
        var queueData = await _cacheService.LoadAsync<QueueCacheData>(_cacheKey);

        if (queueData?.SingerIds is { Count: > 0 })
        {
            _singerIds.AddRange(queueData.SingerIds);
            SelectedSingerId = queueData.SelectedSingerId;
            await ResolveAsync();
            _logger.LogInformation("Singer queue loaded ({Count} singers)", queueData.SingerIds.Count);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            _logger.LogWarning("Singer queue cache was empty or missing");
        }
    }

    private async Task SaveAsync()
    {
        var queueData = new QueueCacheData
        {
            SelectedSingerId = SelectedSingerId,
            SingerIds = _singerIds
        };

        await _cacheService.SaveAsync(_cacheKey, queueData);
    }

    private async Task ResolveAsync()
    {
        var resolved = new List<Singer>(_singerIds.Count);

        foreach (var id in _singerIds)
        {
            var singer = await _singersService.ReadAsync(id);
            if (singer is not null)
                resolved.Add(singer);
        }

        _cachedSingers = resolved;
    }

    private async Task NotifyAsync()
    {
        await ResolveAsync();
        await SaveAsync();

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public class ServiceOptions
    {
        public const string SectionName = nameof(SingerQueueService);
        public bool PromptBeforeRemovingSinger { get; init; }
    }

    private class QueueCacheData
    {
        public Guid? SelectedSingerId { get; set; }
        public List<Guid> SingerIds { get; set; } = [];
    }
}
