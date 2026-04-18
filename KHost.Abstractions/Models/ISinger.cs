namespace KHost.Abstractions.Models;

public interface ISinger
{
    Guid Id { get; }
    string Name { get; set; }
    List<IQueuedSong> SongQueue { get; }
    bool IsTipper { get; set; }
    bool IsRegular { get; set; }
    bool IsPerforming { get; set; }
    string Notes { get; set; }
}
