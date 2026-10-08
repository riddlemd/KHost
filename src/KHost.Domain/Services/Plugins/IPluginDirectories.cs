namespace KHost.Domain.Services.Plugins;

/// <summary>Where the host keeps plugin folders; host-internal because a plugin must not see
/// another plugin's or the staging folder.</summary>
public interface IPluginDirectories
{
    string PluginsDirectory { get; }
}
