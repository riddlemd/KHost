namespace KHost.UserInterface.Services;

/// <summary>What the media importer's browser lists of a folder. Every call completes off the
/// caller's thread, so a slow disk or a network share never stalls the console.</summary>
public interface IMediaFolderReader
{
    /// <summary>Whether <paramref name="path"/> names a folder that can be read; false on any failure.</summary>
    Task<bool> ExistsAsync(string path);

    /// <summary>The folder's subfolders, and its files whose extension is in
    /// <paramref name="extensions"/>.</summary>
    /// <exception cref="UnauthorizedAccessException">The folder cannot be listed.</exception>
    /// <exception cref="IOException">The folder is gone or its share dropped.</exception>
    Task<MediaFolderListing> ReadAsync(string path, IReadOnlySet<string> extensions);

    /// <summary>How many files directly inside each folder have an extension in
    /// <paramref name="extensions"/>. A folder that cannot be read is left out.</summary>
    Task<IReadOnlyDictionary<string, int>> CountFilesAsync(
        IReadOnlyList<string> folders, IReadOnlySet<string> extensions, CancellationToken cancellationToken);
}

public sealed record MediaFolderListing(IReadOnlyList<MediaFolderItem> Folders, IReadOnlyList<MediaFolderItem> Files);

/// <param name="Size">Zero for a folder.</param>
public sealed record MediaFolderItem(string FullPath, long Size, DateTime Modified);
