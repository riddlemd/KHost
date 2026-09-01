using KHost.Abstractions.Models.Plugins;
using KHost.Common.Plugins;

namespace KHost.Abstractions.Services;

/// <summary>
/// Downloads a catalog release, verifies it, and stages it for the next start. Nothing installs
/// into a running host: loading is restart-based, and on Windows a loaded plugin's assembly is
/// locked by this process, so the payload is parked outside <c>plugins/</c> until then.
/// </summary>
public interface IPluginInstallerService
{
    /// <summary>Installs this process has run, active first then settled, newest first.</summary>
    IReadOnlyList<PluginInstallInfo> Snapshot();

    /// <summary>What the staging folder holds for the next start, read fresh from disk.</summary>
    PluginStagingState Staged();

    /// <summary>
    /// Downloads and stages a release. The checksum is verified and the manifest inside the zip is
    /// checked against the catalog entry before anything is staged; a failure leaves the installed
    /// plugin untouched.
    /// </summary>
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
