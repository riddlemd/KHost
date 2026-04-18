using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Models;
using Microsoft.Extensions.Options;

namespace KHost.Domain.Services;

public class PlaybackService : IPlaybackService
{
    private Timer? _timer;
    private DateTime _lastTick;
    private readonly ISingerQueueService _singerQueueService;

    public event Action? StateChanged;

    public IQueuedSong? CurrentQueuedSong { get; private set; }
    public PlaybackState State { get; private set; } = PlaybackState.Stopped;
    public TimeSpan Position { get; private set; }
    public IOptionsMonitor<ServiceOptions> Options { get; set; }

    public PlaybackService(IOptionsMonitor<ServiceOptions> options, ISingerQueueService singerQueueService)
    {
        Options = options;
        _singerQueueService = singerQueueService;
    }

    public void Load(QueuedSong song)
    {
        ResetState();
        CurrentQueuedSong = song;
        Position = TimeSpan.Zero;
        StateChanged?.Invoke();
    }

    public async Task PlayAsync()
    {
        if (CurrentQueuedSong is null || State == PlaybackState.Playing) return;

        // Move the song's singer to the top of the queue when play starts
        if (CurrentQueuedSong.Singer?.Id is { } singerId)
        {
            await _singerQueueService.MoveSingerToStartAsync(singerId);
            CurrentQueuedSong.Singer.IsPerforming = true;
        }

        State = PlaybackState.Playing;
        CurrentQueuedSong.Status = QueuedSongStatus.Playing;
        _lastTick = DateTime.UtcNow;
        _timer?.Dispose();
        _timer = new Timer(OnTick, null, 500, 500);
        StateChanged?.Invoke();
    }

    public void Pause()
    {
        if (State != PlaybackState.Playing) return;
        if (CurrentQueuedSong?.Singer != null)
            CurrentQueuedSong.Singer.IsPerforming = false;
        State = PlaybackState.Paused;
        _timer?.Dispose();
        _timer = null;
        StateChanged?.Invoke();
    }

    public async Task StopAsync()
    {
        ResetState();
        await EndedAsync();
        StateChanged?.Invoke();
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer?.Dispose();
            _timer = null;
        }
    }

    void IPlaybackService.Load(IQueuedSong song)
    {
        if (song is QueuedSong queuedSong)
            Load(queuedSong);
    }

    private void ResetState()
    {
        _timer?.Dispose();
        _timer = null;

        if (CurrentQueuedSong?.Singer != null)
            CurrentQueuedSong.Singer.IsPerforming = false;

        State = PlaybackState.Stopped;
        Position = TimeSpan.Zero;
    }

    private async Task EndedAsync()
    {
        var currentQueuedSong = CurrentQueuedSong;

        CurrentQueuedSong = null;

        if (currentQueuedSong?.Id is not { } singerId)
            return;


        if (Options.CurrentValue.MoveSingerToBottomAfterPerformance)
        {
            await _singerQueueService.MoveSingerToEndAsync(singerId);
            await _singerQueueService.SelectFirstSingerInQueueAsync();
        }

        if (currentQueuedSong?.Id is not { } queuedSongId)
            return;

        await _singerQueueService.RemoveQueuedSongAsync(singerId, queuedSongId);
    }

    private void OnTick(object? state) => _ = TickAsync();

    private async Task TickAsync()
    {
        var now = DateTime.UtcNow;
        Position += now - _lastTick;
        _lastTick = now;

        if (CurrentQueuedSong?.Song.Duration is { } duration && Position >= duration)
        {
            Position = duration;
            CurrentQueuedSong.Status = QueuedSongStatus.Played;
            if (CurrentQueuedSong.Singer != null)
                CurrentQueuedSong.Singer.IsPerforming = false;
            State = PlaybackState.Stopped;
            _timer?.Dispose();
            _timer = null;

            await EndedAsync();
        }

        StateChanged?.Invoke();
    }

    public class ServiceOptions
    {
        public const string SectionName = nameof(PlaybackService);

        public bool MoveSingerToBottomAfterPerformance { get; init; }
    }
}
