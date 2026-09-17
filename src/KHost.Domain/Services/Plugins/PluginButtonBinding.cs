using KHost.Abstractions.Services;

namespace KHost.Domain.Services.Plugins;

/// <summary>Ties a plugin button handler to its id, since the container tracks no owner otherwise.</summary>
public sealed record PluginButtonBinding(string PluginId, IPluginButtonHandler Handler);
