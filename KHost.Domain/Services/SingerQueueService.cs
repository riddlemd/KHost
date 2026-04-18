using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Models;
using Microsoft.Extensions.Options;
using System.Reflection;
using System.Text.Json;

namespace KHost.Domain.Services;

public class SingerQueueService : ISingerQueueService
{
    private const string _cacheKey = "singer-queue";

    private static readonly PropertyInfo QueuedSongSingerProp =
        typeof(QueuedSong).GetProperty(nameof(QueuedSong.Singer))!;

    private readonly ICacheService _cacheService;
    private readonly List<Singer> _singers = [];

    public event Action? StateChanged;

    public IReadOnlyList<ISinger> Singers => _singers.AsReadOnly();
    public Guid? SelectedSingerId { get; private set; }
    public ISinger? SelectedSinger =>
        SelectedSingerId is { } id ? _singers.FirstOrDefault(s => s.Id == id) : null;

    public Guid? SelectedQueuedSongId { get; private set; }
    public IQueuedSong? SelectedQueuedSong =>
        SelectedQueuedSongId is { } id ? SelectedSinger?.SongQueue.FirstOrDefault(s => s.Id == id) : null;

    public ISinger? CurrentlyPerformingSinger => _singers.FirstOrDefault(x => x.IsPerforming);

    public IOptionsMonitor<ServiceOptions> Options { get; set; }

    public SingerQueueService(IOptionsMonitor<ServiceOptions> options, ICacheService cacheService)
    {
        Options = options;
        _cacheService = cacheService;
        Load();
    }

    public async Task SelectSingerAsync(Guid? singerId)
    {
        SelectedSingerId = singerId;
        SelectedQueuedSongId = null;
        await NotifyAsync();
    }

    public async Task SelectSongAsync(Guid? queuedSongId)
    {
        if (SelectedQueuedSong?.Status == QueuedSongStatus.Played) return;

        SelectedQueuedSongId = queuedSongId;
        await NotifyAsync();
    }

    public async Task<ISinger> AddSingerAsync(string name)
    {
        var singer = new Singer { Id = Guid.NewGuid(), Name = name };

        _singers.Add(singer);

        await NotifyAsync();

        return singer;
    }

    public async Task RemoveSingerAsync(Guid singerId)
    {
        _singers.RemoveAll(s => s.Id == singerId);

        if (SelectedSingerId == singerId)
            SelectedSingerId = null;

        await NotifyAsync();
    }

    public async Task AddSongAsync(Guid singerId, SongSearchEntity song)
    {
        var singer = _singers.FirstOrDefault(s => s.Id == singerId);
        if (singer is null) return;
        singer.SongQueue.Add(new QueuedSong
        {
            Singer = singer,
            Song = new Song
            {
                FilePath = song.FilePath,
                Title = song.DisplayName
            }
        });
        await NotifyAsync();
    }

    public async Task RemoveQueuedSongAsync(Guid singerId, Guid queuedSongId)
    {
        var singer = _singers.FirstOrDefault(s => s.Id == singerId);
        singer?.SongQueue.RemoveAll(q => q.Id == queuedSongId);
        await NotifyAsync();
    }

    public async Task MoveSingerUpAsync(Guid singerId)
    {
        if (IsCurrentlyPerforming(singerId)) return;
        var idx = _singers.FindIndex(s => s.Id == singerId);
        if (idx > 0) (_singers[idx], _singers[idx - 1]) = (_singers[idx - 1], _singers[idx]);
        await NotifyAsync();
    }

    public async Task MoveSingerDownAsync(Guid singerId)
    {
        if (IsCurrentlyPerforming(singerId)) return;
        var idx = _singers.FindIndex(s => s.Id == singerId);
        if (idx >= 0 && idx < _singers.Count - 1) (_singers[idx], _singers[idx + 1]) = (_singers[idx + 1], _singers[idx]);
        await NotifyAsync();
    }

    public async Task MoveSingerToStartAsync(Guid singerId)
    {
        if (IsCurrentlyPerforming(singerId)) return;
        var idx = _singers.FindIndex(s => s.Id == singerId);
        if (idx > 0)
        {
            var singer = _singers[idx];
            _singers.RemoveAt(idx);
            _singers.Insert(0, singer);
            await NotifyAsync();
        }
    }

    public async Task MoveSingerToEndAsync(Guid singerId)
    {
        if (IsCurrentlyPerforming(singerId)) return;
        var idx = _singers.FindIndex(s => s.Id == singerId);
        if (idx >= 0 && idx < _singers.Count - 1)
        {
            var singer = _singers[idx];
            _singers.RemoveAt(idx);
            _singers.Add(singer);
            await NotifyAsync();
        }
    }

    public async Task SelectFirstSingerInQueueAsync()
    {
        var firstSinger = _singers.FirstOrDefault();

        if (firstSinger == null) return;

        await SelectSingerAsync(firstSinger.Id);
    }

    public async Task MoveQueuedSongUpAsync(Guid singerId, Guid queuedSongId)
    {
        var singer = _singers.FirstOrDefault(s => s.Id == singerId);
        if (singer is null) return;
        var idx = singer.SongQueue.FindIndex(q => q.Id == queuedSongId);
        if (idx > 0) (singer.SongQueue[idx], singer.SongQueue[idx - 1]) = (singer.SongQueue[idx - 1], singer.SongQueue[idx]);
        await NotifyAsync();
    }

    public async Task MoveQueuedSongDownAsync(Guid singerId, Guid queuedSongId)
    {
        var singer = _singers.FirstOrDefault(s => s.Id == singerId);
        if (singer is null) return;
        var idx = singer.SongQueue.FindIndex(q => q.Id == queuedSongId);
        if (idx >= 0 && idx < singer.SongQueue.Count - 1) (singer.SongQueue[idx], singer.SongQueue[idx + 1]) = (singer.SongQueue[idx + 1], singer.SongQueue[idx]);
        await NotifyAsync();
    }

    public async Task MoveQueuedSongToEndAsync(Guid singerId, Guid queuedSongId)
    {
        var singer = _singers.FirstOrDefault(s => s.Id == singerId);
        if (singer is null) return;
        var idx = singer.SongQueue.FindIndex(q => q.Id == queuedSongId);
        if (idx >= 0 && idx < singer.SongQueue.Count - 1)
        {
            var song = singer.SongQueue[idx];
            singer.SongQueue.RemoveAt(idx);
            singer.SongQueue.Add(song);
        }
        await NotifyAsync();
    }

    public async Task ToggleSingerIsRegularAsync(Guid singerId)
    {
        var singer = _singers.FirstOrDefault(s => s.Id == singerId);
        if (singer is null) return;
        singer.IsRegular = !singer.IsRegular;
        await NotifyAsync();
    }

    public async Task ToggleSingerIsTipperAsync(Guid singerId)
    {
        var singer = _singers.FirstOrDefault(s => s.Id == singerId);
        if (singer is null) return;
        singer.IsTipper = !singer.IsTipper;
        await NotifyAsync();
    }

    async Task ISingerQueueService.AddSongAsync(Guid singerId, ISongSearchEntity song)
    {
        if (song is SongSearchEntity entity)
            await AddSongAsync(singerId, entity);
    }

    private async void Load()
    {
        var queueData = await _cacheService.LoadAsync<QueueCacheData>(_cacheKey);

        if (queueData?.Singers != null)
        {
            _singers.Clear();
            _singers.AddRange(queueData.Singers);
            SelectedSingerId = queueData.SelectedSingerId;
            RebuildSingerReferences();
        }
    }

    private void RebuildSingerReferences()
    {
        foreach (var singer in _singers.OfType<Singer>())
            foreach (var qs in singer.SongQueue.OfType<QueuedSong>())
                QueuedSongSingerProp.SetValue(qs, singer);
    }

    private async Task SaveAsync()
    {
        var queueData = new QueueCacheData
        {
            SelectedSingerId = SelectedSingerId,
            Singers = _singers
        };

        await _cacheService.SaveAsync(_cacheKey, queueData);
    }

    private async Task NotifyAsync()
    {
        await SaveAsync();
        StateChanged?.Invoke();
    }

    private bool IsCurrentlyPerforming(Guid singerId) =>
        CurrentlyPerformingSinger?.Id == singerId;

    public class ServiceOptions
    {
        public const string SectionName = nameof(SingerQueueService);

        public bool PromptBeforeRemovingSinger { get; init; }
    }

    private class QueueCacheData
    {
        public Guid? SelectedSingerId { get; set; }
        public List<Singer> Singers { get; set; } = [];
    }
}