using KHost.Abstractions.Services;
using KHost.Domain.Services.Plugins;

namespace KHost.Domain.Services;

public sealed class HostDirectories : IHostDirectories
{
    public HostDirectories(string? binDirectory = null)
    {
        BinDirectory = binDirectory ?? PluginPaths.Bin;
    }

    public string BinDirectory { get; }
}
