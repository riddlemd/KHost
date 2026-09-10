namespace KHost.Abstractions.Models;

public class KHostUser : RepositoryModel
{
    public required string Name { get; set; }

    /// <summary>The name as it is compared and looked up. Written by the persistence layer, not by hand.</summary>
    public string NameFolded { get; set; } = string.Empty;

    public string Notes { get; set; } = "";
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public string? PasswordHash { get; set; }

    public ICollection<KHostUserGroup> Groups { get; set; } = [];
    public ICollection<Tip> Tips { get; set; } = [];

    /// <summary>
    /// What providers outside KHost call this singer. Read back by
    /// <see cref="Services.IUsersService.ReadByForeignKeyAsync"/>, which is how a returning guest
    /// is recognised without matching on a name they may have typed differently.
    /// </summary>
    public ICollection<KHostUserForeignKey> ForeignKeys { get; set; } = [];
}
