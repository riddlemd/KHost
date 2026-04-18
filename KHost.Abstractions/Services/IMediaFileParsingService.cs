using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

public interface IMediaFileParsingService
{
    ISong LoadAndParse(string filePath);
}
