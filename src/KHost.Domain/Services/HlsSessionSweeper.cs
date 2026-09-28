using System.Diagnostics;

namespace KHost.Domain.Services;

/// <summary>Removes stream session folders a crashed or killed host left behind.</summary>
internal static class HlsSessionSweeper
{
    /// <summary>Names the process that owns a session folder. Dotted, so it never reads as a segment.</summary>
    internal const string OwnerFileName = ".khost-owner";

    /// <summary>For a folder with no owner file, from a build that wrote none: long past any song
    /// still being encoded, whose writes keep the folder's time fresh.</summary>
    internal static readonly TimeSpan UnownedMaxAge = TimeSpan.FromHours(1);

    internal static void WriteOwner(string sessionDirectory)
        => File.WriteAllText(Path.Combine(sessionDirectory, OwnerFileName), Environment.ProcessId.ToString());

    /// <summary>Deletes every session folder under <paramref name="root"/> whose owner is not running,
    /// or that has no owner and has not been written for <see cref="UnownedMaxAge"/>.</summary>
    /// <returns>The folder names deleted.</returns>
    internal static IReadOnlyList<string> Sweep(string root, DateTime nowUtc, Func<int, bool> isRunning)
    {
        var swept = new List<string>();
        var rootInfo = new DirectoryInfo(root);

        // A linked root sends the sweep somewhere nobody configured it to look.
        if (!rootInfo.Exists || rootInfo.LinkTarget is not null) return swept;

        foreach (var folder in rootInfo.EnumerateDirectories())
        {
            // Only names this service mints: a WorkingDirectory pointed at a shared folder must not
            // lose everything in it. A link is never followed.
            if (folder.LinkTarget is not null || !Guid.TryParseExact(folder.Name, "N", out _)) continue;

            if (!IsOrphaned(folder, nowUtc, isRunning)) continue;

            // Recursive delete removes a link inside the folder, not what it points at.
            try
            {
                folder.Delete(recursive: true);
                swept.Add(folder.Name);
            }
            // A segment still held open, as Windows refuses; the next start tries again.
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        return swept;
    }

    internal static bool IsRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch
        {
            // Unable to ask is not proof of death; keeping a folder costs only disk.
            return true;
        }
    }

    private static bool IsOrphaned(DirectoryInfo folder, DateTime nowUtc, Func<int, bool> isRunning)
    {
        var owner = new FileInfo(Path.Combine(folder.FullName, OwnerFileName));

        if (owner.Exists && owner.LinkTarget is null)
        {
            string text;
            try { text = File.ReadAllText(owner.FullName); }
            catch (IOException) { return false; }

            if (int.TryParse(text.Trim(), out var processId))
                return !isRunning(processId);
        }

        return nowUtc - folder.LastWriteTimeUtc > UnownedMaxAge;
    }
}
