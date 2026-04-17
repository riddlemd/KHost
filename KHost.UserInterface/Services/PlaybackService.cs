using KHost.UserInterface.Models;

namespace KHost.UserInterface.Services;

public enum PlaybackState { Stopped, Playing, Paused }

public class PlaybackService : IDisposable
{
    public event Action? StateChanged;

    public QueuedSong? CurrentSong { get; private set; }
    public PlaybackState State { get; private set; } = PlaybackState.Stopped;
    public TimeSpan Position { get; private set; }

    private Timer? _timer;
    private DateTime _lastTick;
    private readonly SingerQueueService _queueService;

    public PlaybackService(SingerQueueService queueService)
    {
        _queueService = queueService;
    }

    public void Load(QueuedSong song)
    {
        StopInternal();
        CurrentSong = song;
        Position = TimeSpan.Zero;
        StateChanged?.Invoke();
    }

    public void Play()
    {
        if (CurrentSong is null || State == PlaybackState.Playing) return;

        // Move the song's singer to the top of the queue when play starts
        if (CurrentSong.Singer?.Id is { } singerId)
        {
            var singerIndex = _queueService.Singers.ToList().FindIndex(s => s.Id == singerId);
            while (singerIndex > 0)
            {
                _queueService.MoveSingerUp(singerId);
                singerIndex--;
            }
            CurrentSong.Singer.IsPerforming = true;
        }

        State = PlaybackState.Playing;
        CurrentSong.Status = SongStatus.Playing;
        _lastTick = DateTime.UtcNow;
        _timer?.Dispose();
        _timer = new Timer(Tick, null, 500, 500);
        StateChanged?.Invoke();
    }

    public void Pause()
    {
        if (State != PlaybackState.Playing) return;
        if (CurrentSong?.Singer != null)
            CurrentSong.Singer.IsPerforming = false;
        State = PlaybackState.Paused;
        _timer?.Dispose();
        _timer = null;
        StateChanged?.Invoke();
    }

    public void Stop()
    {
        StopInternal();
        StateChanged?.Invoke();
    }

    private void StopInternal()
    {
        _timer?.Dispose();
        _timer = null;
        if (CurrentSong?.Status == SongStatus.Playing)
            CurrentSong.Status = SongStatus.Pending;
        if (CurrentSong?.Singer != null)
            CurrentSong.Singer.IsPerforming = false;
        State = PlaybackState.Stopped;
        Position = TimeSpan.Zero;
    }

    private void Tick(object? state)
    {
        var now = DateTime.UtcNow;
        Position += now - _lastTick;
        _lastTick = now;

        if (CurrentSong?.Song.Duration is { } duration && Position >= duration)
        {
            Position = duration;
            CurrentSong.Status = SongStatus.Played;
            if (CurrentSong.Singer != null)
                CurrentSong.Singer.IsPerforming = false;
            State = PlaybackState.Stopped;
            _timer?.Dispose();
            _timer = null;
        }

        StateChanged?.Invoke();
    }

    public void Dispose() => _timer?.Dispose();
}
