namespace KHost.Abstractions.Models;

/// <summary>A person known to KHost, usually a singer.</summary>
public class KHostUser : RepositoryModel
{
    /// <summary>The name shown throughout the app.</summary>
    public required string Name { get; set; }

    /// <summary>Free text about this person, not shown to the room.</summary>
    public string Notes { get; set; } = "";

    /// <summary>When the account was created, UTC.</summary>
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    /// <summary>The groups this user belongs to.</summary>
    public ICollection<KHostUserGroup> Groups { get; set; } = [];

    /// <summary>Tips recorded for this person.</summary>
    public ICollection<Tip> Tips { get; set; } = [];

    /// <summary>How outside providers identify this singer, so a returning guest is matched rather
    /// than added again under a new name.</summary>
    public ICollection<KHostUserForeignKey> ForeignKeys { get; set; } = [];
}
