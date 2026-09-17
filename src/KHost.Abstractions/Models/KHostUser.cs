namespace KHost.Abstractions.Models;

public class KHostUser : RepositoryModel
{
    public required string Name { get; set; }

    /// <summary>The name as compared and looked up. Written by the persistence layer.</summary>
    public string NameFolded { get; set; } = string.Empty;

    public string Notes { get; set; } = "";
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public string? PasswordHash { get; set; }

    public ICollection<KHostUserGroup> Groups { get; set; } = [];
    public ICollection<Tip> Tips { get; set; } = [];

    /// <summary>What outside providers call this singer; read via ReadByForeignKeyAsync.</summary>
    public ICollection<KHostUserForeignKey> ForeignKeys { get; set; } = [];
}
