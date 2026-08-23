namespace KHost.Abstractions.Services;

/// <summary>
/// Host-side entry point for cancelling a plugin's in-flight download. Not part of the
/// plugin-facing SDK contract — the queue side (and shutdown) inject this to kill a download the
/// plugin owns via the token handed out in its <c>ImportTicket</c>.
/// </summary>
public interface IPluginImportCancellation
{
    /// <summary>Cancels the registered download for this media id, if one is in flight. No-op otherwise.</summary>
    Task CancelImportAsync(Guid mediaId);

    /// <summary>Cancels every in-flight download at once, so none outlives the host on shutdown.</summary>
    void CancelAllImports();
}
