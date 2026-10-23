using KHost.Common.Storage;

namespace KHost.UnitTests.Common.Storage;

public sealed class AppPathsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "khost-apppaths-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Resolve_OnMacOS_IsApplicationSupport()
        => Assert.Equal(
            Path.Combine("/Users/someone", "Library", "Application Support", "KHost"),
            AppPaths.Resolve(null, isMacOS: true, "/Applications/KHost.app/Contents/MacOS/", "/Users/someone"));

    [Fact]
    public void Resolve_ElsewhereThanMacOS_IsBesideTheExecutable()
        => Assert.Equal("/opt/khost/", AppPaths.Resolve(null, isMacOS: false, "/opt/khost/", "/home/someone"));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Resolve_Overridden_IsTheOverrideOnEveryOs(bool isMacOS)
        => Assert.Equal(Path.GetFullPath("/tmp/khost-data"), AppPaths.Resolve("/tmp/khost-data", isMacOS, "/opt/khost/", "/home/someone"));

    [Fact]
    public void AdoptLegacyData_NewDataRoot_CopiesTheCacheAndPlugins()
    {
        var legacy = Path.Combine(_root, "legacy");
        var data = Path.Combine(_root, "data");
        Write(Path.Combine(legacy, "cache", "khost.db"), "library");
        Write(Path.Combine(legacy, "plugins", "lrclib", "plugin.dll"), "plugin");
        Write(Path.Combine(legacy, "logs", "host.log"), "log");

        Assert.True(AppPaths.AdoptLegacyData(legacy, data));

        Assert.Equal("library", File.ReadAllText(Path.Combine(data, "cache", "khost.db")));
        Assert.Equal("plugin", File.ReadAllText(Path.Combine(data, "plugins", "lrclib", "plugin.dll")));
        Assert.False(Directory.Exists(Path.Combine(data, "logs")));
    }

    /// <summary>A rebuilt dev bundle carries the old copy again: it must not replace the live library.</summary>
    [Fact]
    public void AdoptLegacyData_DataRootAlreadyHoldsData_CopiesNothing()
    {
        var legacy = Path.Combine(_root, "legacy");
        var data = Path.Combine(_root, "data");
        Write(Path.Combine(legacy, "cache", "khost.db"), "old");
        Write(Path.Combine(data, "cache", "khost.db"), "live");

        Assert.False(AppPaths.AdoptLegacyData(legacy, data));

        Assert.Equal("live", File.ReadAllText(Path.Combine(data, "cache", "khost.db")));
    }

    /// <summary>Windows and Linux keep their data beside the executable: there is nowhere to move it, even before the
    /// first run has made a library.</summary>
    [Fact]
    public void AdoptLegacyData_SameFolder_CopiesNothing()
    {
        Write(Path.Combine(_root, "plugins", "lrclib", "plugin.dll"), "plugin");

        Assert.False(AppPaths.AdoptLegacyData(_root, _root));
    }

    [Fact]
    public void AdoptLegacyData_NothingToAdopt_ReportsNothing()
        => Assert.False(AppPaths.AdoptLegacyData(Path.Combine(_root, "empty"), Path.Combine(_root, "data")));

    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }
}
