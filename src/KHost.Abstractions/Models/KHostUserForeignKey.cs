namespace KHost.Abstractions.Models;

/// <summary>How something outside KHost names this singer, so a returning guest isn't added twice.</summary>
/// <remarks><see cref="Source"/> plus <see cref="Key"/> together identify at most one user, so a
/// caller can look one up and trust the match.</remarks>
public class KHostUserForeignKey : RepositoryModel
{
    /// <summary>The <see cref="KHostUser"/> this key names.</summary>
    public Guid UserId { get; set; }

    /// <summary>Who issued the key: the provider's own <c>SourceName</c>.</summary>
    public required string Source { get; set; }

    /// <summary>The provider's own id, matched exactly, never folded, since it is nobody's name.</summary>
    public required string Key { get; set; }

    /// <summary>Marks a key naming a connection, not a person; every ephemeral key dies at startup.</summary>
    public bool IsEphemeral { get; set; }
}
