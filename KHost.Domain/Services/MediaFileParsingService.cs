using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Models;
using TagFile = TagLib.File;

namespace KHost.Domain.Services
{
    public class MediaFileParsingService : IMediaFileParsingService
    {
        public ISong LoadAndParse(string filePath)
        {
            var file = TagFile.Create(filePath);
            var tag = file.Tag;

            return new Song
            {
                FilePath = filePath,
                Title = tag.Title ?? Path.GetFileNameWithoutExtension(filePath),
                Artist = tag.FirstPerformer ?? "Unknown Artist",
                Album = tag.Album ?? "Unknown Album",
                Duration = TimeSpan.FromSeconds(file.Properties.Duration.TotalSeconds),
                Format = Path.GetExtension(filePath).TrimStart('.').ToUpperInvariant(),
                Status = SongStatus.Ready,
                DateAdded = DateTime.UtcNow,
                DateModified = DateTime.UtcNow
            };
        }
    }
}
