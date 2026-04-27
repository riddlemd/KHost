namespace KHost.Abstractions.Models;

public class KHostUserGroup
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; set; }
    public string Description { get; set; } = "";
    public bool IsAdmin { get; set; }

    public List<KHostPermission> Permissions { get; set; } = [];

    public ICollection<KHostUser> Users { get; set; } = [];
}
