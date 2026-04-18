using KHost.Abstractions.Models;

namespace KHost.Domain.Models;

public class Venue : IVenue
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public bool Enabled { get; set; } = true;
    public required string Name { get; set; }
    public string Notes { get; set; } = "";
    public DateTimeOffset LastUpdated { get; set; }
}
