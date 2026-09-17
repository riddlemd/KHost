using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

public interface IMediaFileParsingService
{
    /// <summary>Type decides which fields apply; only a performance has a performer.</summary>
    Task<Media> LoadAndParseAsync(string filePath, MediaType type = MediaType.Karaoke);
    (string Title, string? Artist) GetTitleAndArtistFromFilename(string filePath);
}
