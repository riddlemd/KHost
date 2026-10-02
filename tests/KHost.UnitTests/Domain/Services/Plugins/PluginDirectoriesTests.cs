using KHost.Domain.Services.Plugins;

namespace KHost.UnitTests.Domain.Services.Plugins;

public class PluginDirectoriesTests
{
    private readonly PluginDirectories _directories = new();

    [Fact]
    public void Directories_ReturnThePathsTheCompositionCodeUses()
    {
        Assert.Equal(PluginPaths.Plugins, _directories.PluginsDirectory);
        Assert.Equal(PluginPaths.Staging, _directories.StagingDirectory);
        Assert.Equal(PluginPaths.Cache, _directories.CacheDirectory);
    }
}
