using KHost.Abstractions.Models.Plugins;

namespace KHost.Abstractions.Services;

/// <summary>Downloads, verifies, and stages a release; nothing installs into a running host.</summary>
/// <remarks>Host-only: the Plugins page's install and remove buttons. A plugin has no business with
/// it. Everything staged takes effect on the next start. A host singleton, callable from any thread.
/// Announces <see cref="KHost.Abstractions.Messaging.Messages.PluginInstallsChanged"/> on every
/// state change, on progress only when the whole percentage moves, and on every staging
/// change.</remarks>
public interface IPluginInstallerService
{
    /// <summary>Installs this process has run, active first then settled, newest first.</summary>
    IReadOnlyList<PluginInstallInfo> Snapshot();

    /// <summary>What the staging folder holds for the next start, read fresh from disk.</summary>
    PluginStagingState Staged();

    /// <summary>Downloads and stages a release, checksum and manifest checked; else untouched.</summary>
    /// <returns>How the install ended — staged, failed with a reason, or cancelled; failures come back
    /// here rather than as exceptions. A release without an https URL and a checksum fails at once.
    /// While an install for the same plugin is already running, its current state is returned and
    /// nothing new starts.</returns>
    Task<PluginInstallInfo> InstallAsync(PluginCatalogEntry entry, PluginCatalogRelease release);

    /// <summary>Cancels an in-flight download. No-op for an id with none.</summary>
    /// <remarks>Only signals: the install settles itself as Cancelled.</remarks>
    void Cancel(Guid pluginId);

    /// <summary>Cancels every in-flight download, so none outlives the host on shutdown.</summary>
    void CancelAll();

    /// <summary>Marks one installed folder for deletion on the next start.</summary>
    /// <remarks>By folder name, not plugin id: two folders may carry one id, and a host removes the
    /// row it pointed at.</remarks>
    void MarkForRemoval(string pluginFolderName);

    /// <summary>Drops a staged install or a failed stage for this id, and any pending removal of
    /// every folder carrying it.</summary>
    void ClearStaged(Guid pluginId);

    /// <summary>Drops the pending removal of one folder, leaving any other copy's alone.</summary>
    void ClearRemoval(string pluginFolderName);
}
