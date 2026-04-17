namespace KHost.UserInterface.Models;

public enum SongStatus { Pending, Playing, Played }

public class QueuedSong
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Singer Singer { get; init; }
    public required Song Song { get; init; }
    public SongStatus Status { get; set; } = SongStatus.Pending;

    // Convenience properties that delegate to Song
    public string FilePath => Song.FilePath;
}
