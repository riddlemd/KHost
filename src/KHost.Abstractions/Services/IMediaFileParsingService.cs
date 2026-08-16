using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

public interface IMediaFileParsingService
{
    Task<Media> LoadAndParseAsync(string filePath);
    (string Title, string? Artist) GetTitleAndArtistFromFilename(string filePath);
}
