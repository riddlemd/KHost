namespace KHost.Abstractions.Models;

/// <summary>Who asked for a song to be queued, so rules meant for guests leave the host alone.</summary>
/// <remarks>Values are pinned: a plugin compiles them in as numbers.</remarks>
public enum EnqueueOrigin
{
    /// <summary>The host, at the console or through a provider acting on the host's click.</summary>
    Host = 0,

    /// <summary>A guest, from their phone or any other remote sign-up.</summary>
    Remote = 1,
}
