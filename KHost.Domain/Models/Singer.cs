using KHost.Abstractions.Models;

namespace KHost.Domain.Models;

public class Singer : ISinger
{
    public Guid Id { get; init; }
    public required string Name { get; set; }
    public List<IQueuedSong> SongQueue { get; init; } = [];
    public bool IsTipper { get; set; }
    public bool IsRegular { get; set; }
    public bool IsPerforming { get; set; }
    public string Notes { get; set; } = "";
}
