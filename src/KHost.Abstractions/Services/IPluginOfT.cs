namespace KHost.Abstractions.Services;

/// <summary>A plugin's entry point that names its settings class, so the host can hand those settings
/// to every part of the plugin through the options pattern.</summary>
/// <remarks>The host binds <typeparamref name="TSettings"/> to this plugin's saved settings over the
/// manifest's defaults (each manifest key to the property of the same name, ignoring case) and serves
/// <c>IOptions&lt;TSettings&gt;</c>, <c>IOptionsSnapshot&lt;TSettings&gt;</c> and
/// <c>IOptionsMonitor&lt;TSettings&gt;</c> to the plugin's constructors. A save on the Plugins page
/// reaches <c>IOptionsMonitor.CurrentValue</c> and its <c>OnChange</c> listeners at once, with no
/// restart; <c>IOptions</c> keeps the value it first read.
///
/// <para><typeparamref name="TSettings"/> must be declared in the plugin's own entry assembly, and a
/// plugin names one. A plugin without settings implements plain <see cref="IPlugin"/>, or no entry
/// point at all.</para></remarks>
/// <typeparam name="TSettings">The plugin's settings class.</typeparam>
public interface IPlugin<TSettings> : IPlugin where TSettings : class, new()
{
    /// <inheritdoc />
    /// <remarks>Does nothing unless the plugin has something to start; one that only names its
    /// settings writes no body of its own.</remarks>
    Task IPlugin.InitializeAsync(IPluginContext context, CancellationToken cancellationToken) => Task.CompletedTask;
}
