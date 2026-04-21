using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using TagFile = TagLib.File;

namespace KHost.Domain.Services
{
    public class MediaFileParsingService : IMediaFileParsingService
    {
        public Media LoadAndParse(string filePath)
        {
            var file = TagFile.Create(filePath);
            var tag = file.Tag;

            return new Media
            {
                FilePath = filePath,
                Title = tag.Title ?? Path.GetFileNameWithoutExtension(filePath),
                Artist = tag.FirstPerformer ?? "Unknown Artist",
                Duration = TimeSpan.FromSeconds(file.Properties.Duration.TotalSeconds),
                Format = Path.GetExtension(filePath).TrimStart('.').ToUpperInvariant(),
                Status = MediaStatus.Ready,
                DateAdded = DateTime.UtcNow,
            };
        }
    }
}
