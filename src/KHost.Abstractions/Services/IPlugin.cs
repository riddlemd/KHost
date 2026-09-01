namespace KHost.Abstractions.Services;

/// <summary>
/// A plugin's entry point, and optional: a plugin that only exposes providers needs none, and the
/// host loads it exactly as before. Implement this to do work at startup or to tell the host
/// something about how the plugin is set up.
/// </summary>
/// <remarks>
/// Loading happens in two phases because the host discovers plugins before its container exists.
/// Discovery and registration come first and run no plugin code; <see cref="InitializeAsync"/>
/// runs afterwards, once services are available. A plugin that throws here is marked as errored
/// and its providers are left registered — one plugin's bad setup never stops the app.
/// </remarks>
public interface IPlugin
{
    /// <summary>
    /// Runs once, after the host is built and before the console is used. Keep it short: the
    /// window a host stares at while this runs is startup. Anything slow belongs on a background
    /// task this method starts rather than awaits.
    /// </summary>
    Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken = default);
}
