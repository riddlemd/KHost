namespace KHost.UserInterface.Models;

public class Singer
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; set; }
    public List<QueuedSong> SongQueue { get; } = [];
    public bool IsTipper { get; set; } = true;
    public bool IsRegular { get; set; } = true;
    public bool IsPerforming { get; set; } = false;
    public string Notes { get; set; } = "";
}
