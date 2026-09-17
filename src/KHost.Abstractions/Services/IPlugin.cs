namespace KHost.Abstractions.Services;

/// <summary>A plugin's entry point; optional if it only exposes providers.</summary>
/// <remarks>Runs once services exist; a throw here marks it errored, providers stay registered.</remarks>
public interface IPlugin
{
    /// <summary>Runs once at startup; keep it short, and start slow work as a background task.</summary>
    Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken = default);
}
