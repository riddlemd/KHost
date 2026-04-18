using KHost.Abstractions.Models;

namespace KHost.Domain.Models;

public class QueuedSong : IQueuedSong
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required ISinger Singer { get; init; }
    public required ISong Song { get; init; }
    public QueuedSongStatus Status { get; set; } = QueuedSongStatus.Pending;

    ISinger IQueuedSong.Singer => Singer;
    ISong IQueuedSong.Song => Song;

    // Convenience properties that delegate to Song
    public string FilePath => Song.FilePath;
}
