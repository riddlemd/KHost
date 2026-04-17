using KHost.UserInterface.Models;

namespace KHost.UserInterface.Services;

public class SongSearchService
{
    private static readonly HashSet<string> SupportedExtensions =
    [
        ".mp4", ".mkv", ".avi", ".mov", ".wmv",
        ".flv", ".webm", ".ts", ".m4v", ".mpg", ".mpeg",
        ".cdg"
    ];

    public List<SongFile> Search(string directory, string? filter = null, bool recursive = true)
    {
        if (!Directory.Exists(directory))
            return [];

        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var results = new List<SongFile>();

        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*", searchOption))
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (!SupportedExtensions.Contains(ext)) continue;

                var name = Path.GetFileNameWithoutExtension(file);
                if (!string.IsNullOrWhiteSpace(filter) &&
                    !name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    continue;

                results.Add(new SongFile(file, name));
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (DirectoryNotFoundException) { }

        return [.. results.OrderBy(f => f.DisplayName, StringComparer.OrdinalIgnoreCase)];
    }
}
