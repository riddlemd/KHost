namespace KHost.Domain.Services.Plugins;

public sealed class PluginDirectories : IPluginDirectories
{
    public string PluginsDirectory => PluginPaths.Plugins;
}
