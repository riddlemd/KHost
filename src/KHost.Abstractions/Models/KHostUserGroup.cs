namespace KHost.Abstractions.Models;

public class KHostUserGroup : RepositoryModel
{
    public static readonly Guid AdminGroupId   = new("00000000-0000-0000-0000-000000000001");
    public static readonly Guid RegularGroupId = new("00000000-0000-0000-0000-000000000002");

    public required string Name { get; set; }

    /// <summary>The name as search matches it. Written by the persistence layer, not by hand.</summary>
    public string NameFolded { get; set; } = string.Empty;
    public string Description { get; set; } = "";
    public bool IsAdmin { get; set; }

    /// <summary>
    /// Members of this group are not singers — a login account rather than someone on the roster —
    /// so the singer queue leaves them out of its suggestions.
    /// </summary>
    public bool ExcludeFromSingerQueue { get; set; }

    public List<KHostPermission> Permissions { get; set; } = [];

    public ICollection<KHostUser> Users { get; set; } = [];
}
