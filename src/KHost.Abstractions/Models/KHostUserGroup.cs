namespace KHost.Abstractions.Models;

/// <summary>A named set of members, e.g. the singers a VIP rotation favours.</summary>
public class KHostUserGroup : RepositoryModel
{
    /// <summary>Id of the built-in Admin group, for staff left out of the singer queue.</summary>
    public static readonly Guid AdminGroupId   = new("00000000-0000-0000-0000-000000000001");

    /// <summary>Id of the built-in group every new user starts in.</summary>
    public static readonly Guid RegularGroupId = new("00000000-0000-0000-0000-000000000002");

    /// <summary>The name shown throughout the app.</summary>
    public required string Name { get; set; }

    /// <summary>Case- and accent-insensitive form of <see cref="Name"/>, used to match it; the host
    /// keeps this in sync, so a plugin should treat it as read-only.</summary>
    public string NameFolded { get; set; } = string.Empty;

    /// <summary>Free text describing the group's purpose.</summary>
    public string Description { get; set; } = "";

    /// <summary>Members are not singers; left out of the queue's suggestions.</summary>
    public bool ExcludeFromSingerQueue { get; set; }

    /// <summary>The users belonging to this group.</summary>
    public ICollection<KHostUser> Users { get; set; } = [];
}
