using System.Diagnostics;

namespace KHost.IntegrationTests;

/// <summary>
/// Several suites skip themselves when the tool they drive is absent, which makes a run that lost
/// their coverage look exactly like a clean one. This is the counterweight: it fails, once and in
/// plain words, rather than letting the summary claim more than it tested.
/// </summary>
public class EnvironmentCoverageTests
{
    /// <summary>Set when you knowingly cannot install the tooling and want the run to proceed.</summary>
    private const string OptOutVariable = "KHOST_SKIP_ENVIRONMENT_TESTS";

    [Fact]
    public void Ffmpeg_IsInstalled_OrTheTranscodeTestsCoveredNothing()
    {
        if (Environment.GetEnvironmentVariable(OptOutVariable) is { Length: > 0 }) return;

        Assert.True(
            IsOnPath("ffmpeg"),
            $"ffmpeg is not on PATH, so every transcode test skipped and this run proved nothing "
            + $"about streaming. README lists it as a prerequisite. Install it, or set "
            + $"{OptOutVariable}=1 to accept the gap.");
    }

    [Fact]
    public void Ffprobe_IsInstalled_OrTheStreamAssertionsCannotRun()
    {
        if (Environment.GetEnvironmentVariable(OptOutVariable) is { Length: > 0 }) return;

        Assert.True(
            IsOnPath("ffprobe"),
            $"ffprobe is not on PATH, so the tests that check a segment really carries audio "
            + $"cannot run. Install it, or set {OptOutVariable}=1 to accept the gap.");
    }

    private static bool IsOnPath(string executable)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(executable, "-version")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });

            process!.WaitForExit();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
