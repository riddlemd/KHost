namespace KHost.Abstractions.Models;

/// <summary>A person known to KHost: a singer, a login account, or both.</summary>
public class KHostUser : RepositoryModel
{
    /// <summary>The name shown throughout the app.</summary>
    public required string Name { get; set; }

    /// <summary>Case- and accent-insensitive form of <see cref="Name"/>, used to match it; the host
    /// keeps this in sync, so a plugin should treat it as read-only.</summary>
    public string NameFolded { get; set; } = string.Empty;

    /// <summary>Free text about this person, not shown to the room.</summary>
    public string Notes { get; set; } = "";

    /// <summary>When the account was created, UTC.</summary>
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    /// <summary>The account's hashed password. Null for a singer with no login of their own.</summary>
    public string? PasswordHash { get; set; }

    /// <summary>The groups this user belongs to; permissions are the union of every group's own.</summary>
    public ICollection<KHostUserGroup> Groups { get; set; } = [];

    /// <summary>Tips recorded for this person.</summary>
    public ICollection<Tip> Tips { get; set; } = [];

    /// <summary>How outside providers identify this singer, so a returning guest is matched rather
    /// than added again under a new name.</summary>
    public ICollection<KHostUserForeignKey> ForeignKeys { get; set; } = [];
}
