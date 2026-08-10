using KHost.Domain.Services;

namespace KHost.UnitTests.Domain.Services;

public class LocalScreenProviderTests
{
    [Fact]
    public void ResolveExePath_UsesExtensionlessAppHost_OnNonWindows()
    {
        var path = LocalScreenProvider.ResolveExePath(null, "/app", isWindows: false);

        Assert.Equal(Path.Combine("/app", "KHost.Screen"), path);
    }

    [Fact]
    public void ResolveExePath_UsesExeAppHost_OnWindows()
    {
        var path = LocalScreenProvider.ResolveExePath(null, "/app", isWindows: true);

        Assert.Equal(Path.Combine("/app", "KHost.Screen.exe"), path);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveExePath_FallsBackToBaseDirectory_WhenConfiguredPathIsBlank(string? configured)
    {
        var path = LocalScreenProvider.ResolveExePath(configured, "/app", isWindows: false);

        Assert.Equal(Path.Combine("/app", "KHost.Screen"), path);
    }

    [Fact]
    public void ResolveExePath_PrefersConfiguredPath_WhenProvided()
    {
        var path = LocalScreenProvider.ResolveExePath("/custom/screen-app", "/app", isWindows: false);

        Assert.Equal("/custom/screen-app", path);
    }

    [Fact]
    public void BuildArguments_KeepsScreenIdWithSpacesAsOneArgument()
    {
        var args = LocalScreenProvider.BuildArguments("http://localhost:5251/ipc/screen", "Screen 1");

        Assert.Equal(
            ["--server-uri", "http://localhost:5251/ipc/screen", "--screen-id", "Screen 1"],
            args);
    }

    [Fact]
    public void BuildArguments_DistinguishesScreenIdsThatDifferOnlyAfterASpace()
    {
        var first = LocalScreenProvider.BuildArguments("http://host/ipc", "Screen 1");
        var second = LocalScreenProvider.BuildArguments("http://host/ipc", "Screen 2");

        // Concatenating these would yield the same parsed --screen-id ("Screen") for both,
        // so two screens would collide on one id.
        Assert.NotEqual(first[3], second[3]);
    }

    [Fact]
    public void BuildArguments_PairsEachFlagWithItsValue()
    {
        var args = LocalScreenProvider.BuildArguments("http://host/ipc", "Screen 1");

        Assert.Equal(4, args.Length);
        Assert.Equal("http://host/ipc", args[Array.IndexOf(args, "--server-uri") + 1]);
        Assert.Equal("Screen 1", args[Array.IndexOf(args, "--screen-id") + 1]);
    }
}
