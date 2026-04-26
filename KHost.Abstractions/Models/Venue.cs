namespace KHost.Abstractions.Models;

public class Venue
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public bool Enabled { get; set; } = true;
    public required string Name { get; set; }
    public string Notes { get; set; } = "";
    public string Address { get; set; } = "";
    public string Phone { get; set; } = "";
    public int DefaultVolume { get; set; } = 75;
    public bool MoveSingerToBottomAfterPerformance { get; set; }
    public bool PromptBeforeRemovingSinger { get; set; }
    public bool ClearQueueOnClose { get; set; }
}
