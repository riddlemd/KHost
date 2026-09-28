using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace KHost.Domain.Services.Visualisations;

public class VisualisationPlaylistService : BaseRepositoryService<VisualisationPlaylist, IVisualisationPlaylistRepository>, IVisualisationPlaylistService
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>Per playlist, the entry id picked last: in order, the next is the one after it;
    /// shuffled, it is the one not to pick again.</summary>
    private readonly Dictionary<Guid, Guid> _lastPicked = [];
    private readonly Random _random;

    public VisualisationPlaylistService(ILogger<VisualisationPlaylistService> logger, IVisualisationPlaylistRepository repository,
        IMessageBroker broker, Random? random = null)
        : base(logger, repository, broker, new VisualisationPlaylistsChanged())
    {
        _random = random ?? Random.Shared;
    }

    public Task<VisualisationPlaylist?> ReadWithEntriesAsync(Guid id) => Repository.ReadWithEntriesAsync(id);

    public Task<IReadOnlyList<VisualisationPlaylist>> ReadAllWithEntriesAsync() => Repository.ReadAllWithEntriesAsync();

    public override async Task<bool> DeleteAsync(Guid id)
    {
        var deleted = await base.DeleteAsync(id);
        if (deleted) Forget(id);

        return deleted;
    }

    public async Task<bool> ReplaceEntriesAsync(Guid playlistId, IReadOnlyList<VisualisationEntry> entries)
    {
        if (await Repository.ReadAsync(playlistId) is null)
            return false;

        await Repository.ReplaceEntriesAsync(playlistId, [.. entries.Select(Held)]);

        Forget(playlistId);
        AnnounceChange();

        return true;
    }

    public async Task<VisualisationEntry?> SelectNextAsync(Guid playlistId)
    {
        var playlist = await Repository.ReadWithEntriesAsync(playlistId);
        if (playlist is null || playlist.Entries.Count == 0)
            return null;

        var entries = playlist.Entries;

        await _lock.WaitAsync();
        try
        {
            var lastIndex = _lastPicked.TryGetValue(playlistId, out var last)
                ? entries.FindIndex(entry => entry.Id == last)
                : -1;

            int index;
            if (!playlist.Shuffle)
            {
                index = (lastIndex + 1) % entries.Count;
            }
            else if (lastIndex < 0 || entries.Count == 1)
            {
                index = _random.Next(entries.Count);
            }
            else
            {
                // One fewer choice, skipping the last pick, so no song repeats the one before it.
                index = _random.Next(entries.Count - 1);
                if (index >= lastIndex) index++;
            }

            _lastPicked[playlistId] = entries[index].Id;

            return entries[index];
        }
        finally
        {
            _lock.Release();
        }
    }

    private void Forget(Guid playlistId)
    {
        _lock.Wait();
        try
        {
            _lastPicked.Remove(playlistId);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>A copy with every setting inside its range, so a display never has to guess.</summary>
    private static VisualisationEntry Held(VisualisationEntry entry) => new()
    {
        Id = entry.Id,
        PresetSource = entry.PresetSource,
        PresetName = entry.PresetName,
        Brightness = Math.Clamp(entry.Brightness, VisualisationEntry.MinBrightness, VisualisationEntry.MaxBrightness),
        Saturation = Math.Clamp(entry.Saturation, VisualisationEntry.MinSaturation, VisualisationEntry.MaxSaturation),
        Sensitivity = Math.Clamp(entry.Sensitivity, VisualisationEntry.MinSensitivity, VisualisationEntry.MaxSensitivity),
        DarkenBehindWords = entry.DarkenBehindWords,
    };
}
