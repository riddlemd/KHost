using KHost.Abstractions.Models.Plugins;

namespace KHost.Abstractions.Services;

/// <summary>Draws and runs a plugin's buttons via its <see cref="IPluginButtonHandler"/>.</summary>
public interface IPluginButtonService
{
    /// <summary>Buttons in manifest order, paired with the handler's current look; hidden ones cut.</summary>
    IReadOnlyList<(PluginButtonDefinition Definition, PluginButtonState State)> ButtonsFor(string pluginId);

    /// <summary>Runs a plugin's button. An unknown plugin or key is a no-op.</summary>
    Task InvokeAsync(string pluginId, string key, CancellationToken cancellationToken = default);
}
