namespace KHost.Abstractions.Services;

/// <summary>Runs every loaded plugin's entry point, once, after the host is built.</summary>
public interface IPluginInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
