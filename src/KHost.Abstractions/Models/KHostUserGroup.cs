namespace KHost.Abstractions.Models;

public class KHostUserGroup : RepositoryModel
{
    public static readonly Guid AdminGroupId   = new("00000000-0000-0000-0000-000000000001");
    public static readonly Guid RegularGroupId = new("00000000-0000-0000-0000-000000000002");

    private string _name = string.Empty;

    /// <summary>Setting this refolds <see cref="NameFolded"/> with it, so the two cannot drift apart.</summary>
    public required string Name
    {
        get => _name;
        set
        {
            _name = value;
            NameFolded = TextFolding.Fold(value);
        }
    }

    /// <summary>The name as search matches it: composed and lowercased.</summary>
    public string NameFolded { get; private set; } = string.Empty;
    public string Description { get; set; } = "";
    public bool IsAdmin { get; set; }

    public List<KHostPermission> Permissions { get; set; } = [];

    public ICollection<KHostUser> Users { get; set; } = [];
}
