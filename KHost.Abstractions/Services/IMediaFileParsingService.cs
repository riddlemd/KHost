using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

public interface IMediaFileParsingService
{
    Task<Media> LoadAndParse(string filePath);
    (string Title, string? Artist) GetTitleAndArtistFromFilename(string filePath);
}
