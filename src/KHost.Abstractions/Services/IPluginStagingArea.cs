using KHost.Abstractions.Models.Plugins;

namespace KHost.Abstractions.Services;

/// <summary>Payloads and removals staged until the next start; loading is restart-based.</summary>
/// <remarks>Host-only; a plugin has no business with it. Keeps no state of its own beyond what is on
/// disk, so a stage made before a crash still applies. Installs are keyed by plugin id; removals by
/// folder name, since two folders can carry one id. Callable from any thread. Announces
/// nothing.</remarks>
public interface IPluginStagingArea
{
    /// <summary>What is staged, read fresh from disk so a stage made before a crash still shows.</summary>
    PluginStagingState Read();

    /// <summary>Moves staged payloads into <c>plugins/</c>; a failure is set aside, not retried.</summary>
    /// <remarks>Run once at startup, before any plugin is discovered. Removals apply first, so a
    /// removal and a reinstall of one id amount to an update; an install replaces every folder
    /// carrying its id. Never throws: a failed install is set aside with its reason, and a removal
    /// that fails is tried again next start.</remarks>
    void ApplyPending();

    /// <summary>Scratch space for one install's download and extraction, on the same volume as
    /// staging so the move into it never crosses a filesystem.</summary>
    /// <remarks>Not created by this call.</remarks>
    string WorkPathFor(Guid pluginId);

    /// <summary>Takes over a validated payload, replacing anything already staged for this id.</summary>
    /// <remarks>The folder at <paramref name="payloadRoot"/> is moved, not copied, so it is gone
    /// afterwards. Also clears any failed stage and any pending removal for the id, which would
    /// otherwise undo it.</remarks>
    void Stage(string payloadRoot, Guid pluginId);

    /// <summary>Marks one installed folder for deletion on the next start. A name that does not
    /// resolve to a direct child of <c>plugins/</c> is ignored rather than followed.</summary>
    void MarkForRemoval(string pluginFolderName);

    /// <summary>Drops a staged install or a failed stage for this id, and any pending removal of
    /// every folder carrying it.</summary>
    void Clear(Guid pluginId);

    /// <summary>Drops the pending removal of one folder, leaving any other copy's alone.</summary>
    void ClearRemoval(string pluginFolderName);
}
