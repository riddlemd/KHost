using System.Text.RegularExpressions;
using KHost.Abstractions.Models;

namespace KHost.Domain.Services.FFmpeg;

/// <summary>Where the host looks for ffmpeg and ffprobe, and how it reads their versions.</summary>
internal static partial class FFmpegLocator
{
    /// <summary>The file name a program has on disk: <c>ffmpeg</c>, or <c>ffmpeg.exe</c> on Windows.</summary>
    public static string ExecutableName(FFmpegTool tool, bool windows)
        => ToolName(tool) + (windows ? ".exe" : "");

    /// <summary>The name the manifest and the log use.</summary>
    public static string ToolName(FFmpegTool tool) => tool == FFmpegTool.FFmpeg ? "ffmpeg" : "ffprobe";

    /// <summary>The first existing executable in the configured folder, the bin folder, then PATH.
    /// </summary>
    /// <remarks>A configured folder without the program falls through rather than failing: the
    /// setting names a place to look first, not the only place.</remarks>
    public static string? Resolve(
        FFmpegTool tool, string? configuredFolder, string? binFolder, string? pathVariable, bool windows)
    {
        var name = ExecutableName(tool, windows);

        foreach (var folder in Folders(configuredFolder, binFolder, pathVariable))
        {
            var candidate = Path.Combine(folder, name);

            if (File.Exists(candidate))
                return Path.GetFullPath(candidate);
        }

        return null;
    }

    /// <summary>The version from a <c>-version</c> run's first line, or null when it is not one.</summary>
    /// <remarks>Builds spell it differently (<c>9.0</c>, <c>9.0.2-tessus</c>,
    /// <c>2026-09-19-git-abc</c>, <c>n7.1</c>), so it is whatever follows the word "version".</remarks>
    public static string? ParseVersion(string? output)
    {
        if (string.IsNullOrWhiteSpace(output)) return null;

        var match = VersionLine().Match(output);

        return match.Success ? match.Groups["version"].Value : null;
    }

    private static IEnumerable<string> Folders(string? configuredFolder, string? binFolder, string? pathVariable)
    {
        if (!string.IsNullOrWhiteSpace(configuredFolder))
            yield return configuredFolder.Trim();

        if (!string.IsNullOrWhiteSpace(binFolder))
            yield return binFolder;

        foreach (var entry in (pathVariable ?? "").Split(Path.PathSeparator))
        {
            // Windows allows a quoted PATH entry, and the quotes are not part of the folder.
            var folder = entry.Trim().Trim('"');

            if (folder.Length > 0)
                yield return folder;
        }
    }

    [GeneratedRegex(@"^\s*ff(mpeg|probe) version (?<version>\S+)", RegexOptions.CultureInvariant)]
    private static partial Regex VersionLine();
}
