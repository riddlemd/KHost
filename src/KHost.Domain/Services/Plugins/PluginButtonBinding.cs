using KHost.Abstractions.Services;

namespace KHost.Domain.Services.Plugins;

/// <summary>
/// Ties a plugin's button handler to its id at registration, the one moment the loader knows both.
/// Host-internal: the container otherwise does not record which plugin a handler belongs to.
/// </summary>
public sealed record PluginButtonBinding(string PluginId, IPluginButtonHandler Handler);
