namespace KHost.Abstractions.Models;

/// <summary>
/// How something outside KHost names this singer — a provider's own id for them, so a returning
/// guest reaches the row they already have instead of a second one beside it.
/// </summary>
/// <remarks>
/// <see cref="Source"/> and <see cref="Key"/> carry the same two things
/// <see cref="MediaSearchEntity"/> does, and for the same reason: the provider says who it is, and
/// then says what it calls the thing. A pair is unique across the whole table — two singers must
/// never both claim one external identity, which is the guarantee that makes looking one up worth
/// anything.
/// </remarks>
public class KHostUserForeignKey : RepositoryModel
{
    public Guid UserId { get; set; }

    /// <summary>Who issued the key — a provider's <c>SourceName</c>, e.g. "KaraFun".</summary>
    public required string Source { get; set; }

    /// <summary>
    /// The provider's own id for this singer. Opaque: matched exactly, never folded, because it is
    /// nobody's name and a provider is free to make case meaningful.
    /// </summary>
    public required string Key { get; set; }

    /// <summary>
    /// The key names a connection rather than a person, and does not outlive whatever issued it.
    /// KaraFun's is the case it exists for: a guest's id belongs to the phone on the remote
    /// channel, and nothing says it is the same id when they come back tomorrow.
    ///
    /// Anything may delete an ephemeral key, and nothing may rely on one once its issuer is gone —
    /// the host clears them all on the way up, since by definition none can have survived the
    /// restart, and that is also what stops a plugin's rows outliving the plugin.
    /// </summary>
    public bool IsEphemeral { get; set; }
}
