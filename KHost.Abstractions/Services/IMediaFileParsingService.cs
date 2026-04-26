using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

public interface IMediaFileParsingService
{
    Media LoadAndParse(string filePath);
    (string Title, string? Artist) GetTitleAndArtistFromFilename(string filePath);
}
