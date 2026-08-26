using KHost.Abstractions.Models.Plugins;

namespace KHost.Abstractions.Services;

/// <summary>
/// The folder holding plugin payloads and removals until the next start can apply them. Nothing
/// changes <c>plugins/</c> while the host is running: loading is restart-based, and a loaded
/// plugin's assembly is locked by this process on Windows.
/// </summary>
public interface IPluginStagingArea
{
    /// <summary>What is staged, read fresh from disk so a stage made before a crash still shows.</summary>
    PluginStagingState Read();

    /// <summary>
    /// Moves staged payloads into <c>plugins/</c> and deletes what is marked for removal. Called
    /// once at startup, before discovery; a payload that cannot be applied is set aside with its
    /// reason rather than retried on every start.
    /// </summary>
    void ApplyPending();

    /// <summary>Scratch space for one install's download and extraction, on the same volume as
    /// staging so the move into it never crosses a filesystem.</summary>
    string WorkPathFor(Guid pluginId);

    /// <summary>Takes over a validated payload folder, replacing anything already staged for this id.</summary>
    void Stage(string payloadRoot, Guid pluginId);

    /// <summary>Marks an installed plugin for deletion on the next start.</summary>
    void MarkForRemoval(Guid pluginId);

    /// <summary>Drops a staged install, a pending removal, or a failed stage for this id.</summary>
    void Clear(Guid pluginId);
}
