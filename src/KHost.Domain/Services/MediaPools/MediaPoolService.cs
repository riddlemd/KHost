using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.Abstractions.Services;
using KHost.Plugins.Sdk.Messaging;
using KHost.Plugins.Sdk.Messaging.Messages;
using Microsoft.Extensions.Logging;

namespace KHost.Domain.Services.MediaPools;

public class MediaPoolService : BaseRepositoryService<MediaPool, IMediaPoolRepository>, IMediaPoolService
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly Dictionary<Guid, PoolSelectionState> _selectionStates = [];
    private readonly Random _random;

    protected override object? StateChangedMessage => new MediaPoolsChanged();

    public MediaPoolService(ILogger<MediaPoolService> logger, IMediaPoolRepository repository, IMessageBroker broker, Random? random = null)
        : base(logger, repository, broker)
    {
        _random = random ?? Random.Shared;
    }

    public Task<MediaPool?> ReadWithEntriesAsync(Guid id) => Repository.ReadWithEntriesAsync(id);

    public Task<IReadOnlyList<MediaPool>> ReadAllWithEntriesAsync(PoolPurpose purpose, Guid? venueId)
        => Repository.ReadAllWithEntriesAsync(purpose, venueId);

    public async Task<bool> ReplaceEntriesAsync(Guid poolId, IReadOnlyList<MediaPoolEntry> entries)
    {
        var pool = await Repository.ReadWithEntriesAsync(poolId);

        if (pool is null)
            return false;

        // Checked against the edit rather than what is stored: the entries being saved are the
        // ones that could close a loop.
        var edited = new MediaPool
        {
            Id = pool.Id,
            Name = pool.Name,
            Purpose = pool.Purpose,
            Entries = [.. entries],
        };

        var siblings = await Repository.ReadAllWithEntriesAsync(pool.Purpose, pool.VenueId);

        if (MediaPoolCycles.CreatesCycle(edited, siblings.ToDictionary(p => p.Id)))
        {
            Logger.LogWarning("Refused pool {PoolId} entries: they would let the pool reach itself", poolId);
            return false;
        }

        await Repository.ReplaceEntriesAsync(poolId, entries);

        ResetSelection(poolId);

        InvokeStateChanged();

        return true;
    }

    public async Task<MediaPoolEntry?> SelectNextAsync(Guid poolId, Guid? venueId)
    {
        var pool = await Repository.ReadWithEntriesAsync(poolId);

        if (pool is null)
            return null;

        var pools = await Repository.ReadAllWithEntriesAsync(pool.Purpose, venueId);
        var byId = pools.ToDictionary(p => p.Id);

        // The root is read separately and may be scoped to another venue, so it is added rather
        // than assumed present — selection would otherwise stop at a pool it cannot resolve.
        byId[pool.Id] = pool;

        await _lock.WaitAsync();
        try
        {
            if (!_selectionStates.TryGetValue(poolId, out var state))
                _selectionStates[poolId] = state = new PoolSelectionState();

            return MediaPoolSelector.SelectNext(pool, byId, state, _random);
        }
        finally
        {
            _lock.Release();
        }
    }

    public void ResetSelection(Guid poolId)
    {
        _lock.Wait();
        try
        {
            _selectionStates.Remove(poolId);
        }
        finally
        {
            _lock.Release();
        }
    }
}
