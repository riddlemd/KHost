namespace KHost.Abstractions.Models;

public class KHostUserGroup : RepositoryModel
{
    public required string Name { get; set; }
    public string Description { get; set; } = "";
    public bool IsAdmin { get; set; }

    public List<KHostPermission> Permissions { get; set; } = [];

    public ICollection<KHostUser> Users { get; set; } = [];

    public static class Defaults
    {
        public static readonly Guid AdminGroupId   = new("00000000-0000-0000-0000-000000000001");
        public static readonly Guid RegularGroupId = new("00000000-0000-0000-0000-000000000002");
        public static readonly Guid TipperGroupId  = new("00000000-0000-0000-0000-000000000003");

        // Seeded and referenced by id elsewhere — Admin gates permissions and Regular drives the
        // singer panel's regular toggle, so removing either leaves those lookups pointing at nothing.
        public static bool IsDefault(Guid groupId)
            => groupId == AdminGroupId || groupId == RegularGroupId || groupId == TipperGroupId;
    }
}
