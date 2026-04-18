using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Models;

namespace KHost.Domain.Services;

public class SongSearchService : ISongSearchService
{
    private static readonly HashSet<string> SupportedExtensions =
    [
        ".mp4", ".mkv", ".avi", ".mov", ".wmv",
        ".flv", ".webm", ".ts", ".m4v", ".mpg", ".mpeg",
        ".cdg"
    ];

    public Task<List<ISongSearchEntity>> SearchAsync(string directory, string? filter = null, bool recursive = true)
    {
        return Task.Run(() =>
        {
            if (!Directory.Exists(directory))
                return [];

            var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var results = new List<ISongSearchEntity>();

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

                    var format = ext switch
                    {
                        ".cdg" => "CDG+MP3",
                        _ => ext[1..].ToUpperInvariant()
                    };

                    results.Add(new SongSearchEntity { DisplayName = name, FilePath = file, Format = format });
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (DirectoryNotFoundException) { }

            return results.OrderBy(f => f.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        });
    }

    async Task<List<ISongSearchEntity>> ISongSearchService.SearchAsync(string directory, string? filter = null, bool recursive = true)
    {
        var results = await SearchAsync(directory, filter, recursive);
        return results.Cast<ISongSearchEntity>().ToList();
    }
}
