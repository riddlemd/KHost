namespace KHost.Abstractions.Services;

/// <summary>Runs every loaded plugin's entry point, once, after the host is built.</summary>
/// <remarks>Host-only; a plugin has no business with it. The host calls it once at startup.</remarks>
public interface IPluginInitializer
{
    /// <summary>Calls each loaded plugin's <see cref="IPlugin.InitializeAsync"/> in turn.</summary>
    /// <remarks>Never throws for a plugin's failure: that plugin is marked errored, with the reason,
    /// and the rest still run.</remarks>
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
