using KHost.Abstractions.Models.Plugins;

namespace KHost.Abstractions.Services;

/// <summary>Draws and runs a plugin's buttons via its <see cref="IPluginButtonHandler"/>.</summary>
/// <remarks>Host-owned: what the Plugins page calls. A plugin implements
/// <see cref="IPluginButtonHandler"/>, not this. A host singleton, callable from any thread.
/// Announces nothing.</remarks>
public interface IPluginButtonService
{
    /// <summary>Buttons in manifest order, paired with the handler's current look; hidden ones cut.</summary>
    /// <returns>Empty for a plugin with no handler or no manifest.</returns>
    IReadOnlyList<(PluginButtonDefinition Definition, PluginButtonState State)> ButtonsFor(string pluginId);

    /// <summary>Runs a plugin's button. An unknown plugin or key is a no-op.</summary>
    /// <remarks>Whatever the handler throws is passed on to the caller.</remarks>
    Task InvokeAsync(string pluginId, string key, CancellationToken cancellationToken = default);
}
