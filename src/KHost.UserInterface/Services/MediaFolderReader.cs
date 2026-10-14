namespace KHost.UserInterface.Services;

public sealed class MediaFolderReader : IMediaFolderReader
{
    public Task<bool> ExistsAsync(string path) => Task.Run(() => Directory.Exists(path));

    public Task<MediaFolderListing> ReadAsync(string path, IReadOnlySet<string> extensions) => Task.Run(() =>
    {
        var folders = Directory.GetDirectories(path)
            .Select(d => new MediaFolderItem(d, 0, Directory.GetLastWriteTime(d)))
            .ToList();

        var files = Directory.GetFiles(path)
            .Where(f => extensions.Contains(Path.GetExtension(f)))
            .Select(f =>
            {
                var info = new FileInfo(f);
                return new MediaFolderItem(f, info.Length, info.LastWriteTime);
            })
            .ToList();

        return new MediaFolderListing(folders, files);
    });

    public Task<IReadOnlyDictionary<string, int>> CountFilesAsync(
        IReadOnlyList<string> folders, IReadOnlySet<string> extensions, CancellationToken cancellationToken)
        => Task.Run<IReadOnlyDictionary<string, int>>(() =>
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var folder in folders)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    counts[folder] = Directory.EnumerateFiles(folder).Count(f => extensions.Contains(Path.GetExtension(f)));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }

            return counts;
        }, cancellationToken);
}
