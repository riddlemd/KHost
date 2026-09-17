using KHost.Abstractions.Models.Plugins;

namespace KHost.Abstractions.Services;

/// <summary>Downloads, verifies, and stages a release; nothing installs into a running host.</summary>
public interface IPluginInstallerService
{
    /// <summary>Installs this process has run, active first then settled, newest first.</summary>
    IReadOnlyList<PluginInstallInfo> Snapshot();

    /// <summary>What the staging folder holds for the next start, read fresh from disk.</summary>
    PluginStagingState Staged();

    /// <summary>Downloads and stages a release, checksum and manifest checked; else untouched.</summary>
    Task<PluginInstallInfo> InstallAsync(PluginCatalogEntry entry, PluginCatalogRelease release);

    /// <summary>Cancels an in-flight download. No-op for an id with none.</summary>
    void Cancel(Guid pluginId);

    /// <summary>Cancels every in-flight download, so none outlives the host on shutdown.</summary>
    void CancelAll();

    /// <summary>Marks one installed folder for deletion on the next start.</summary>
    void MarkForRemoval(string pluginFolderName);

    /// <summary>Drops a staged install or a failed stage for this id, and any pending removal of
    /// every folder carrying it.</summary>
    void ClearStaged(Guid pluginId);

    /// <summary>Drops the pending removal of one folder, leaving any other copy's alone.</summary>
    void ClearRemoval(string pluginFolderName);
}
