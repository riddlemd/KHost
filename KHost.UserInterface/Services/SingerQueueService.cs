using KHost.UserInterface.Models;
using System.Text.Json;

namespace KHost.UserInterface.Services;

public class SingerQueueService : IDisposable
{
    private readonly List<Singer> _singers = [];
    private readonly string _filePath;
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private bool _disposed;

    public event Action? StateChanged;

    public IReadOnlyList<Singer> Singers => _singers;

    public Guid? SelectedSingerId { get; private set; }
    public Singer? SelectedSinger =>
        SelectedSingerId is { } id ? _singers.FirstOrDefault(s => s.Id == id) : null;

    public Guid? SelectedSongId { get; private set; }
    public QueuedSong? SelectedSong =>
        SelectedSongId is { } id ? SelectedSinger?.SongQueue.FirstOrDefault(s => s.Id == id) : null;

    public Singer? CurrentlyPerformingSinger => _singers.FirstOrDefault(x => x.IsPerforming);

    public SingerQueueService(IWebHostEnvironment env)
    {
        _filePath = Path.Combine(env.ContentRootPath, "queue-state.json");
        Load();
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                var queueData = JsonSerializer.Deserialize<QueueData>(json);
                if (queueData?.Singers != null)
                {
                    _singers.Clear();
                    foreach (var singerData in queueData.Singers)
                    {
                        var singer = new Singer { Id = singerData.Id, Name = singerData.Name, IsTipper = singerData.IsTipper, IsRegular = singerData.IsRegular };
                        foreach (var songData in singerData.SongQueue)
                        {
                            singer.SongQueue.Add(new QueuedSong
                            {
                                Id = songData.Id,
                                Singer = singer,
                                Song = new Song
                                {
                                    FilePath = songData.FilePath,
                                    DisplayName = songData.DisplayName
                                },
                                Status = songData.Status
                            });
                        }
                        _singers.Add(singer);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading queue from file: {ex.Message}");
        }
    }

    private async Task SaveAsync()
    {
        await _saveLock.WaitAsync();
        try
        {
            var queueData = new QueueData
            {
                Singers = _singers.Select(s => new SingerData
                {
                    Id = s.Id,
                    Name = s.Name,
                    IsTipper = s.IsTipper,
                    IsRegular = s.IsRegular,
                    SongQueue = s.SongQueue.Select(q => new SongData
                    {
                        Id = q.Id,
                        FilePath = q.FilePath,
                        DisplayName = q.Song.DisplayName,
                        Status = q.Status
                    }).ToList()
                }).ToList()
            };

            var json = JsonSerializer.Serialize(queueData, new JsonSerializerOptions { WriteIndented = true });
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            await File.WriteAllTextAsync(_filePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving queue to file: {ex.Message}");
        }
        finally
        {
            _saveLock.Release();
        }
    }

    private void Notify()
    {
        _ = SaveAsync();
        StateChanged?.Invoke();
    }

    public async Task InitializeAsync()
    {
        await Task.CompletedTask;
    }

    public void SelectSinger(Guid? singerId)
    {
        SelectedSingerId = singerId;
        SelectedSongId = null;
        Notify();
    }

    public void SelectSong(Guid? songId)
    {
        SelectedSongId = songId;
        Notify();
    }

    public Singer AddSinger(string name)
    {
        var singer = new Singer { Name = name };
        _singers.Add(singer);
        Notify();
        return singer;
    }

    public void RemoveSinger(Guid singerId)
    {
        _singers.RemoveAll(s => s.Id == singerId);
        if (SelectedSingerId == singerId)
            SelectedSingerId = null;
        Notify();
    }

    public void AddSong(Guid singerId, SongFile song)
    {
        var singer = _singers.FirstOrDefault(s => s.Id == singerId);
        if (singer is null) return;
        singer.SongQueue.Add(new QueuedSong
        {
            Singer = singer,
            Song = new Song
            {
                FilePath = song.FilePath,
                DisplayName = song.DisplayName
            }
        });
        Notify();
    }

    public void RemoveSong(Guid singerId, Guid songId)
    {
        var singer = _singers.FirstOrDefault(s => s.Id == singerId);
        singer?.SongQueue.RemoveAll(q => q.Id == songId);
        Notify();
    }

    public void MoveSingerUp(Guid singerId)
    {
        if (IsCurrentlyPerforming(singerId)) return;
        var idx = _singers.FindIndex(s => s.Id == singerId);
        if (idx > 0) (_singers[idx], _singers[idx - 1]) = (_singers[idx - 1], _singers[idx]);
        Notify();
    }

    public void MoveSingerDown(Guid singerId)
    {
        if (IsCurrentlyPerforming(singerId)) return;
        var idx = _singers.FindIndex(s => s.Id == singerId);
        if (idx >= 0 && idx < _singers.Count - 1) (_singers[idx], _singers[idx + 1]) = (_singers[idx + 1], _singers[idx]);
        Notify();
    }

    private bool IsCurrentlyPerforming(Guid singerId) =>
        CurrentlyPerformingSinger?.Id == singerId;

    public void MoveSongUp(Guid singerId, Guid songId)
    {
        var singer = _singers.FirstOrDefault(s => s.Id == singerId);
        if (singer is null) return;
        var idx = singer.SongQueue.FindIndex(q => q.Id == songId);
        if (idx > 0) (singer.SongQueue[idx], singer.SongQueue[idx - 1]) = (singer.SongQueue[idx - 1], singer.SongQueue[idx]);
        Notify();
    }

    public void MoveSongDown(Guid singerId, Guid songId)
    {
        var singer = _singers.FirstOrDefault(s => s.Id == singerId);
        if (singer is null) return;
        var idx = singer.SongQueue.FindIndex(q => q.Id == songId);
        if (idx >= 0 && idx < singer.SongQueue.Count - 1) (singer.SongQueue[idx], singer.SongQueue[idx + 1]) = (singer.SongQueue[idx + 1], singer.SongQueue[idx]);
        Notify();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _saveLock?.Dispose();
            _disposed = true;
        }
    }
}

internal class QueueData
{
    public List<SingerData> Singers { get; set; } = [];
}

internal class SingerData
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsTipper { get; set; }
    public bool IsRegular { get; set; }
    public List<SongData> SongQueue { get; set; } = [];
}

internal class SongData
{
    public Guid Id { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public SongStatus Status { get; set; }
}
