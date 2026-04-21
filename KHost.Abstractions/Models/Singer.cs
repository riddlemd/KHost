namespace KHost.Abstractions.Models;

public class Singer
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; set; }
    public bool IsTipper { get; set; }
    public bool IsRegular { get; set; }
    public string Notes { get; set; } = "";
}
