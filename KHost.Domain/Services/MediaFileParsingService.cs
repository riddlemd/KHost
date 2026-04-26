using System.Text.RegularExpressions;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using TagFile = TagLib.File;

namespace KHost.Domain.Services
{
    public class MediaFileParsingService : IMediaFileParsingService
    {
        private static readonly Regex _labelPrefix = new(@"^\[.*?\]\s*", RegexOptions.Compiled);

        private static readonly Regex _trackNumberPrefix = new(
            @"^(?:Track\s+)?\d+(?:\s*[.\)]\s+|\s+-\s+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex _trailingParenthetical = new(
            @"^(.+?)\s*[\(\[](.*?)[\)\]]\s*$",
            RegexOptions.Compiled);

        public Media LoadAndParse(string filePath)
        {
            var (parsedTitle, parsedArtist) = GetTitleAndArtistFromFilename(filePath);
            TimeSpan? duration = null;
            string title = parsedTitle;
            string artist = parsedArtist ?? "Unknown Artist";

            try
            {
                var file = TagFile.Create(AdjustFilePath(filePath));
                var tag = file.Tag;

                title = !string.IsNullOrWhiteSpace(tag.Title) ? tag.Title : parsedTitle;
                artist = !string.IsNullOrWhiteSpace(tag.FirstPerformer) ? tag.FirstPerformer : parsedArtist ?? "Unknown Artist";
                duration = TimeSpan.FromSeconds(file.Properties.Duration.TotalSeconds);
            }
            catch { }

            return new Media
            {
                FilePath = filePath,
                Title = title,
                Artist = artist,
                Duration = duration,
                Format = Path.GetExtension(filePath).TrimStart('.').ToUpperInvariant(),
                Status = MediaStatus.Ready,
                DateAdded = DateTime.UtcNow,
            };
        }

        public (string Title, string? Artist) GetTitleAndArtistFromFilename(string filePath)
        {
            var name = Path.GetFileNameWithoutExtension(filePath);

            // Strip leading bracketed label: "[SBI-1234] "
            name = _labelPrefix.Replace(name, "").Trim();

            // Strip leading track number: "01 - ", "1. ", "(01) ", "Track 01 - "
            name = _trackNumberPrefix.Replace(name, "").Trim();

            // Try " - ", " – ", " — " separator → Title - Artist
            foreach (var sep in new[] { " - ", " – ", " — " })
            {
                var idx = name.IndexOf(sep, StringComparison.Ordinal);

                if (idx > 0)
                {
                    var left = name[..idx].Trim();
                    var right = name[(idx + sep.Length)..].Trim();
                    var rightMatch = _trailingParenthetical.Match(right);
                    if (rightMatch.Success && !string.IsNullOrWhiteSpace(rightMatch.Groups[2].Value))
                        right = rightMatch.Groups[1].Value.Trim();
                    return (left, right);
                }
            }

            // Try "Title (Artist)" or "Title [Artist]" at end
            var match = _trailingParenthetical.Match(name);

            if (match.Success && !string.IsNullOrWhiteSpace(match.Groups[2].Value))
                return (match.Groups[1].Value.Trim(), match.Groups[2].Value.Trim());

            // Try single underscore: "Title_Artist"
            var underscoreIdx = name.IndexOf('_');
            
            if (underscoreIdx > 0 && underscoreIdx == name.LastIndexOf('_'))
                return (name[..underscoreIdx].Trim(), name[(underscoreIdx + 1)..].Trim());

            // Fallback: whole name as title, no artist
            return (name.Trim(), null);
        }

        private static string AdjustFilePath(string filePath)
        {
            if (Path.GetExtension(filePath).Equals(".cdg", StringComparison.OrdinalIgnoreCase))
                return Path.ChangeExtension(filePath, ".mp3");

            return filePath;
        }
    }
}
