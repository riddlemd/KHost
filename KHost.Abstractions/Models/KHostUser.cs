namespace KHost.Abstractions.Models;

public class KHostUser
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; set; }
    public Guid? DefaultVenueId { get; set; }
    public string Notes { get; set; } = "";
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public string? PasswordHash { get; set; }
    public DateTime? LastLoginDate { get; set; }

    public ICollection<KHostUserGroup> Groups { get; set; } = [];
    public ICollection<Tip> Tips { get; set; } = [];
}
