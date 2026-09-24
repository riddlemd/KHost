using KHost.Telemetry;

namespace KHost.UnitTests.Telemetry;

public class KHostLogFilesTests
{
    [Fact]
    public void HostFileName_MatchesTheHostPattern()
    {
        var name = KHostLogFiles.HostFileName(new DateTime(2026, 9, 24, 13, 5, 9));

        Assert.Equal("host-20260924-130509.log", name);
    }

    [Fact]
    public void ScreenFileName_MatchesTheScreenPattern()
    {
        var name = KHostLogFiles.ScreenFileName("Screen1", 4242, new DateTime(2026, 9, 24, 13, 5, 9));

        Assert.Equal("Screen1-20260924-130509-4242.log", name);
    }

    [Fact]
    public void ScreenFileName_TwoScreensInTheSameSecond_ProduceDifferentNames()
    {
        var when = new DateTime(2026, 9, 24, 13, 5, 9);

        var first = KHostLogFiles.ScreenFileName("Screen1", 111, when);
        var second = KHostLogFiles.ScreenFileName("Screen1", 222, when);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void SanitizeForFileName_NormalId_IsUnchanged()
        => Assert.Equal("normal-id", KHostLogFiles.SanitizeForFileName("normal-id"));

    // Which characters are unsafe is platform-defined (Windows rejects ':' and '/'; Unix only
    // '\0' and '/'), so the input is built from the running platform's own invalid-char list
    // rather than a literal that would only prove the point on one OS.
    [Fact]
    public void SanitizeForFileName_PlatformInvalidCharacters_AreFolded()
    {
        var invalid = Path.GetInvalidFileNameChars();
        Assert.NotEmpty(invalid);

        var screenId = $"Screen{invalid[0]}1";

        var sanitized = KHostLogFiles.SanitizeForFileName(screenId);

        Assert.DoesNotContain(invalid[0], sanitized);
        Assert.Equal("Screen_1", sanitized);
    }

    [Fact]
    public void ScreenFileName_UnsafeScreenId_ProducesAValidFileName()
    {
        var invalid = Path.GetInvalidFileNameChars();
        Assert.NotEmpty(invalid);

        var name = KHostLogFiles.ScreenFileName($"Screen{invalid[0]}1", 4242, new DateTime(2026, 9, 24, 13, 5, 9));

        Assert.DoesNotContain(invalid[0], name);
        Assert.Equal("Screen_1-20260924-130509-4242.log", name);
    }

    [Fact]
    public void SweepStaleLogs_KeepsRecentAndNonLogFiles_DeletesOnlyTheOldLog()
    {
        var directory = CreateTempDirectory();

        try
        {
            var eightDaysOld = WriteFileAt(directory, "eight-days.log", DateTime.UtcNow.AddDays(-8));
            var sixDaysOld = WriteFileAt(directory, "six-days.log", DateTime.UtcNow.AddDays(-6));
            var freshNow = WriteFileAt(directory, "now.log", DateTime.UtcNow);
            var nonLog = WriteFileAt(directory, "eight-days.txt", DateTime.UtcNow.AddDays(-8));

            KHostLogFiles.SweepStaleLogs(directory);

            Assert.False(File.Exists(eightDaysOld), "A .log file older than 7 days should be deleted.");
            Assert.True(File.Exists(sixDaysOld), "A .log file inside the 7-day window must survive.");
            Assert.True(File.Exists(freshNow), "A .log file written just now must survive.");
            Assert.True(File.Exists(nonLog), "A non-.log file must never be swept.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SweepStaleLogs_IsKeyedOnLastWrite_NotOnAge8DaysAgoAtCreation()
    {
        // A long-running process's file was created well over a week ago but is still being
        // appended to; the sweep must key off the last write, or a live file gets deleted from
        // under the process still writing to it.
        var directory = CreateTempDirectory();

        try
        {
            var path = Path.Combine(directory, "long-running.log");
            File.WriteAllText(path, "line one");
            File.SetCreationTimeUtc(path, DateTime.UtcNow.AddDays(-30));
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow);

            KHostLogFiles.SweepStaleLogs(directory);

            Assert.True(File.Exists(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // Unix `unlink` removes a directory entry regardless of any open handle, so a real held-open
    // file (FileShare.None) does not reproduce a delete failure the way it does on Windows.
    // Deleting a file on Unix instead needs write permission on the *containing directory*, so
    // clearing that reliably makes File.Delete throw UnauthorizedAccessException there — standing
    // in for "another process still has it" without depending on OS-specific locking semantics.
    [Fact]
    public void SweepStaleLogs_AFileTheSweepCannotDelete_IsSkippedNotThrown()
    {
        var directory = CreateTempDirectory();
        var path = Path.Combine(directory, "locked.log");
        File.WriteAllText(path, "held open");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-8));

        if (OperatingSystem.IsWindows())
        {
            using var handle = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            Assert.Null(Record.Exception(() => KHostLogFiles.SweepStaleLogs(directory)));
        }
        else
        {
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            try
            {
                Assert.Null(Record.Exception(() => KHostLogFiles.SweepStaleLogs(directory)));
            }
            finally
            {
                File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        Assert.True(File.Exists(path), "A file the sweep could not delete must still be there afterwards.");

        Directory.Delete(directory, recursive: true);
    }

    [Fact]
    public void SweepStaleLogs_MissingDirectory_DoesNotThrow()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"khost-log-sweep-missing-{Guid.NewGuid():N}");

        var exception = Record.Exception(() => KHostLogFiles.SweepStaleLogs(directory));

        Assert.Null(exception);
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"khost-log-sweep-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string WriteFileAt(string directory, string fileName, DateTime lastWriteUtc)
    {
        var path = Path.Combine(directory, fileName);
        File.WriteAllText(path, "content");
        File.SetLastWriteTimeUtc(path, lastWriteUtc);
        return path;
    }
}
