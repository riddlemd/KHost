namespace KHost.Domain.Services.Plugins;

public sealed class PluginDirectories : IPluginDirectories
{
    public string PluginsDirectory => PluginPaths.Plugins;

    public string StagingDirectory => PluginPaths.Staging;

    public string CacheDirectory => PluginPaths.Cache;
}
