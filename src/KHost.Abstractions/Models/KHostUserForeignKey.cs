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

    /// <summary>Marks a key naming a connection, not a person.</summary>
    /// <remarks>The host never drops one by itself, a restart included, since a provider's remote
    /// session can outlive the host process. The source that wrote it removes it once the connection
    /// it names is gone, or all of its own at once through
    /// <c>IUsersService.DeleteEphemeralForeignKeysAsync</c>.</remarks>
    public bool IsEphemeral { get; set; }
}
