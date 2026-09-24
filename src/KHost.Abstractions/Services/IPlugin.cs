namespace KHost.Abstractions.Services;

/// <summary>A plugin's entry point; optional if it only exposes providers.</summary>
/// <remarks>Runs once services exist; a throw here marks it errored, providers stay registered.
///
/// <para>A plugin IMPLEMENTS it, once, in its entry assembly; the host finds it by type and
/// constructs it from the host's services, so its constructor may take any Abstractions interface.
/// It is constructed apart from the plugin's extension singletons — a class implementing both this
/// and an extension interface becomes two separate instances. To reach the extension's state, take
/// the extension class itself in this constructor; it resolves to the same singleton the host
/// uses.</para></remarks>
public interface IPlugin
{
    /// <summary>Runs once at startup; keep it short, and start slow work as a background task.</summary>
    /// <param name="context">This plugin's own settings, secrets and warnings; the same values its
    /// extensions are handed.</param>
    /// <param name="cancellationToken">Honour it if it fires; do not rely on it to cut a slow start
    /// short.</param>
    /// <remarks>Plugins are initialised one after another, before the console serves a page, so a
    /// slow one holds up every plugin after it. A throw is logged and shown on the Plugins page as the
    /// plugin's error; it is never rethrown.</remarks>
    Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken = default);
}
