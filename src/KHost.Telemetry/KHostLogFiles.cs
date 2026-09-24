namespace KHost.Telemetry;

/// <summary>
/// Log file naming and retention shared by the host and every screen process. Lives here, not
/// <c>KHost.Common</c>, because a plugin never writes to this folder or names a file in it; both
/// processes already reference this project, so no new reference is needed to reach it.
/// </summary>
public static class KHostLogFiles
{
    /// <summary>How long a log file is kept after its last write, regardless of which process wrote it.</summary>
    public static readonly TimeSpan RetentionAge = TimeSpan.FromDays(7);

    /// <summary>One file per host launch, so two runs never share a file the way one day-named file did.</summary>
    public static string HostFileName(DateTime? launchedAt = null)
        => $"host-{(launchedAt ?? DateTime.Now):yyyyMMdd-HHmmss}.log";

    /// <summary>
    /// One file per screen launch; the pid breaks the tie a timestamp alone can't when two screens
    /// start in the same second.
    /// </summary>
    public static string ScreenFileName(string screenId, int processId, DateTime? launchedAt = null)
        => $"{SanitizeForFileName(screenId)}-{(launchedAt ?? DateTime.Now):yyyyMMdd-HHmmss}-{processId}.log";

    /// <summary>A screen id is operator-typed and may carry characters a path segment can't.</summary>
    public static string SanitizeForFileName(string value)
        => string.Join("_", value.Split(Path.GetInvalidFileNameChars()));

    /// <summary>
    /// Deletes every *.log in <paramref name="logDirectory"/> last written before
    /// <see cref="RetentionAge"/> ago. Keyed on last write, not creation, so a file a long-running
    /// process still holds open survives even though it started well before the cutoff. A file
    /// another process still has open for exclusive access is skipped rather than crashing startup.
    /// </summary>
    public static void SweepStaleLogs(string logDirectory, DateTime? nowUtc = null)
    {
        if (!Directory.Exists(logDirectory))
            return;

        var cutoffUtc = (nowUtc ?? DateTime.UtcNow) - RetentionAge;

        foreach (var file in new DirectoryInfo(logDirectory).GetFiles("*.log"))
        {
            if (file.LastWriteTimeUtc >= cutoffUtc)
                continue;

            try
            {
                file.Delete();
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
