using KHost.Abstractions.Models.Plugins;

namespace KHost.Abstractions.Services;

/// <summary>
/// What the Plugins page uses to draw and run a plugin's buttons. The buttons are declared in each
/// plugin's manifest; this resolves the plugin's own <see cref="IPluginButtonHandler"/> to say how
/// one should look now and to run it when clicked.
/// </summary>
public interface IPluginButtonService
{
    /// <summary>
    /// The buttons to draw for a plugin, in manifest order, each paired with how its handler says
    /// it should look now. A plugin that declared buttons but ships no handler gets none — there
    /// would be nothing to run. A hidden button (<see cref="PluginButtonState.Visible"/> false) is
    /// left out.
    /// </summary>
    IReadOnlyList<(PluginButtonDefinition Definition, PluginButtonState State)> ButtonsFor(string pluginId);

    /// <summary>Runs a plugin's button. An unknown plugin or key is a no-op.</summary>
    Task InvokeAsync(string pluginId, string key, CancellationToken cancellationToken = default);
}
