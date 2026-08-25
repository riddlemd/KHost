using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

public interface IMediaFileParsingService
{
    /// <summary>
    /// Reads what the file can say about itself. <paramref name="type"/> decides which of those
    /// fields mean anything — only a performance has a performer — so it is taken here rather than
    /// stamped on afterwards.
    /// </summary>
    Task<Media> LoadAndParseAsync(string filePath, MediaType type = MediaType.Karaoke);
    (string Title, string? Artist) GetTitleAndArtistFromFilename(string filePath);
}
