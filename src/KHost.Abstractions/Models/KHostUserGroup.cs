namespace KHost.Abstractions.Models;

public class KHostUserGroup : RepositoryModel
{
    // Seeded groups are the only ones with an all-zero id prefix, so the prefix marks a group as
    // built in without anyone having to maintain a list of them.
    private const string BuiltInIdPrefix = "00000000-0000-0000-0000-";

    public static readonly Guid AdminGroupId   = new("00000000-0000-0000-0000-000000000001");
    public static readonly Guid RegularGroupId = new("00000000-0000-0000-0000-000000000002");
    public static readonly Guid TipperGroupId  = new("00000000-0000-0000-0000-000000000003");

    public required string Name { get; set; }
    public string Description { get; set; } = "";
    public bool IsAdmin { get; set; }

    public List<KHostPermission> Permissions { get; set; } = [];

    public ICollection<KHostUser> Users { get; set; } = [];

    public static bool IsBuiltIn(Guid groupId)
        => groupId.ToString().StartsWith(BuiltInIdPrefix, StringComparison.Ordinal);
}
