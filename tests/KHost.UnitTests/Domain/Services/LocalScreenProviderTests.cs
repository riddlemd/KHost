using System.Diagnostics;
using KHost.Domain.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KHost.UnitTests.Domain.Services;

public class LocalScreenProviderTests
{
    [Fact]
    public void ResolveExePath_UsesExtensionlessAppHost_OnNonWindows()
    {
        var path = LocalScreenProvider.ResolveExePath(null, "/app", isWindows: false);

        Assert.Equal(Path.Combine("/app", "KHost.Screen2"), path);
    }

    [Fact]
    public void ResolveExePath_UsesExeAppHost_OnWindows()
    {
        var path = LocalScreenProvider.ResolveExePath(null, "/app", isWindows: true);

        Assert.Equal(Path.Combine("/app", "KHost.Screen2.exe"), path);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveExePath_FallsBackToBaseDirectory_WhenConfiguredPathIsBlank(string? configured)
    {
        var path = LocalScreenProvider.ResolveExePath(configured, "/app", isWindows: false);

        Assert.Equal(Path.Combine("/app", "KHost.Screen2"), path);
    }

    [Fact]
    public void ResolveExePath_PrefersConfiguredPath_WhenProvided()
    {
        var path = LocalScreenProvider.ResolveExePath("/custom/screen-app", "/app", isWindows: false);

        Assert.Equal("/custom/screen-app", path);
    }

    [Fact]
    public void ResolveExePath_AnchorsRelativeConfiguredPath_ToBaseDirectory()
    {
        // Anchoring to the process CWD instead would resolve somewhere else entirely.
        var path = LocalScreenProvider.ResolveExePath("screens/KHost.Screen2", "/app", isWindows: false);

        Assert.Equal(Path.Combine("/app", "screens/KHost.Screen2"), path);
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

    [Fact]
    public void CloseSpawnedScreens_KillsAScreenItStarted()
    {
        var provider = Provider();
        var screen = StartLongLivedProcess();
        var pid = screen.Id;
        provider.Track("Screen 1", screen);

        provider.CloseSpawnedScreens();

        AssertGone(pid);
    }

    [Fact]
    public void CloseSpawnedScreens_ForgetsTheScreensItClosed()
    {
        var provider = Provider();
        var screen = StartLongLivedProcess();
        provider.Track("Screen 1", screen);

        provider.CloseSpawnedScreens();

        Assert.False(provider.IsTracking("Screen 1"));
    }

    [Fact]
    public void CloseSpawnedScreens_IsSafeToRunTwice()
    {
        var provider = Provider();
        var screen = StartLongLivedProcess();
        var pid = screen.Id;
        provider.Track("Screen 1", screen);

        provider.CloseSpawnedScreens();
        provider.CloseSpawnedScreens();

        AssertGone(pid);
    }

    [Fact]
    public void CloseSpawnedScreens_KillsEveryScreen_EvenWhenOneHasAlreadyGone()
    {
        var provider = Provider();
        var alreadyGone = StartLongLivedProcess();
        var stillRunning = StartLongLivedProcess();
        var pid = stillRunning.Id;
        provider.Track("Screen 1", alreadyGone);
        provider.Track("Screen 2", stillRunning);

        alreadyGone.Kill();
        alreadyGone.WaitForExit(5000);

        provider.CloseSpawnedScreens();

        AssertGone(pid);
    }

    [Fact]
    public void Dispose_ClosesSpawnedScreens()
    {
        var provider = Provider();
        var screen = StartLongLivedProcess();
        var pid = screen.Id;
        provider.Track("Screen 1", screen);

        provider.Dispose();

        AssertGone(pid);
    }

    [Fact]
    public void CloseSpawnedScreens_DoesNothing_WhenNoScreenWasStarted()
    {
        var provider = Provider();

        provider.CloseSpawnedScreens();

        Assert.False(provider.IsTracking("Screen 1"));
    }

    // The provider disposes the Process handle once it has killed it, so the original object can no
    // longer answer HasExited. Ask the OS about the pid instead.
    private static void AssertGone(int pid)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var found = Process.GetProcessById(pid);
                if (found.HasExited)
                    return;
            }
            catch (ArgumentException)
            {
                return;
            }

            Thread.Sleep(50);
        }

        Assert.Fail($"Process {pid} was still running.");
    }

    private static LocalScreenProvider Provider()
        => new(
            Options.Create(new LocalScreenProvider.ServiceOptions()),
            NullLogger<LocalScreenProvider>.Instance);

    // A stand-in for a screen: any child process that stays up until it is killed. The command
    // differs per OS so this runs everywhere rather than being skipped off Unix.
    private static Process StartLongLivedProcess()
    {
        var info = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd.exe", "/c timeout /t 60 /nobreak")
            : new ProcessStartInfo("/bin/sleep", "60");

        info.UseShellExecute = false;
        info.CreateNoWindow = true;

        return Process.Start(info) ?? throw new InvalidOperationException("Could not start the stand-in process.");
    }
}
