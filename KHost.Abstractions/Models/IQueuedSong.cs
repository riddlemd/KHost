namespace KHost.Abstractions.Models;

public interface IQueuedSong
{
    Guid Id { get; }
    ISinger Singer { get; }
    ISong Song { get; }
    QueuedSongStatus Status { get; set; }
    string FilePath { get; }
}

public enum QueuedSongStatus { Pending, Playing, Played }
