using KHost.Abstractions.Models.Plugins;

namespace KHost.Abstractions.Services;

/// <summary>Every plugin found at startup, whatever its status. Fixed until restart.</summary>
/// <remarks>Readable without resolving any plugin, which is what lets the host read manifests — a
/// plugin's import formats or standing QR code — before its code runs. A plugin may take it to see
/// what else is installed. A host singleton, callable from any thread. The list never changes, but
/// a plugin whose entry point fails at startup has its status moved to errored.</remarks>
public interface IPluginRegistry
{
    /// <summary>Every plugin folder found, loaded or not, with its manifest when one could be read.
    /// </summary>
    IReadOnlyList<DiscoveredPlugin> Plugins { get; }
}
