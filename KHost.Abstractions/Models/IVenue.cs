namespace KHost.Abstractions.Models;

public interface IVenue
{
    Guid Id { get; }
    bool Enabled { get; set; }
    string Name { get; set; }
    string Notes { get; set; }
    DateTimeOffset LastUpdated { get; set; }
}
