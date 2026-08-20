namespace KHost.Abstractions.Models;

public class KHostUser : RepositoryModel
{
    private string _name = string.Empty;

    /// <summary>
    /// Setting the name refolds <see cref="NameFolded"/> with it, so the two cannot drift apart —
    /// the folded value is what uniqueness and lookups are enforced on.
    /// </summary>
    public required string Name
    {
        get => _name;
        set
        {
            _name = value;
            NameFolded = TextFolding.Fold(value);
        }
    }

    /// <summary>The name as it is compared: composed and lowercased. Indexed, and unique.</summary>
    public string NameFolded { get; private set; } = string.Empty;

    public string Notes { get; set; } = "";
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public string? PasswordHash { get; set; }

    public ICollection<KHostUserGroup> Groups { get; set; } = [];
    public ICollection<Tip> Tips { get; set; } = [];
}
