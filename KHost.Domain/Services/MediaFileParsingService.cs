using System.Text.RegularExpressions;
using FFMpegCore;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KHost.Domain.Services
{
    public class MediaFileParsingService : IMediaFileParsingService
    {
        private static readonly Regex _trailingParenthetical = new(
            @"^(.+?)\s*[\(\[](.*?)[\)\]]\s*$",
            RegexOptions.Compiled);

        private readonly ILogger<MediaFileParsingService> _logger;
        private readonly IOptionsMonitor<ServiceOptions> _options;

        private Regex[] _prefixStrippers = [];
        private Regex[] _titleNoiseStrippers = [];
        private Regex? _featuringRegex;

        public MediaFileParsingService(
            ILogger<MediaFileParsingService> logger,
            IOptionsMonitor<ServiceOptions> options)
        {
            _logger = logger;
            _options = options;
            Rebuild(options.CurrentValue);
            options.OnChange(Rebuild);
        }

        public async Task<Media> LoadAndParse(string filePath)
        {
            var opts = _options.CurrentValue;
            var (parsedTitle, parsedArtist) = GetTitleAndArtistFromFilename(filePath);

            TimeSpan? duration = null;
            string title = parsedTitle;
            string artist = parsedArtist ?? opts.FallbackArtistName;

            try
            {
                // For .cdg files, probe the companion .mp3
                var probeFilePath = Path.GetExtension(filePath).Equals(".cdg", StringComparison.OrdinalIgnoreCase)
                    ? Path.ChangeExtension(filePath, ".mp3")
                    : filePath;

                var probe = await FFProbe.AnalyseAsync(probeFilePath);

                if (probe.Format.Duration > TimeSpan.Zero)
                    duration = probe.Format.Duration;

                if (probe.Format.Tags?.TryGetValue("title", out var probeTitle) == true && !string.IsNullOrWhiteSpace(probeTitle))
                    title = probeTitle;

                if (probe.Format.Tags?.TryGetValue("artist", out var probeArtist) == true && !string.IsNullOrWhiteSpace(probeArtist))
                    artist = probeArtist;
            }
            catch { }

            var media = new Media
            {
                FilePath = filePath,
                Title = title,
                Artist = artist,
                Duration = duration,
                Format = Path.GetExtension(filePath).TrimStart('.').ToUpperInvariant(),
                Status = MediaStatus.Ready,
                DateAdded = DateTime.UtcNow,
            };

            ApplyFeaturingHandling(media, opts);
            media.Title = StripTitleNoise(media.Title);

            return media;
        }

        public (string Title, string? Artist) GetTitleAndArtistFromFilename(string filePath)
        {
            var opts = _options.CurrentValue;
            var name = Path.GetFileNameWithoutExtension(filePath);

            foreach (var stripper in _prefixStrippers)
                name = stripper.Replace(name, "").Trim();

            foreach (var sep in opts.Separators)
            {
                if (string.IsNullOrEmpty(sep))
                    continue;

                var idx = name.IndexOf(sep, StringComparison.Ordinal);
                if (idx <= 0)
                    continue;

                var left = name[..idx].Trim();
                var right = name[(idx + sep.Length)..].Trim();

                return opts.Format == FilenameFormat.ArtistFirst
                    ? (Title: right, Artist: left)
                    : (Title: left, Artist: right);
            }

            // "Title (Artist)" / "Title [Artist]" — order is unambiguous, ignore Format.
            var match = _trailingParenthetical.Match(name);
            if (match.Success && !string.IsNullOrWhiteSpace(match.Groups[2].Value))
                return (match.Groups[1].Value.Trim(), match.Groups[2].Value.Trim());

            // "Title_Artist" — single underscore. Order is conventionally fixed.
            var underscoreIdx = name.IndexOf('_');
            if (underscoreIdx > 0 && underscoreIdx == name.LastIndexOf('_'))
                return (name[..underscoreIdx].Trim(), name[(underscoreIdx + 1)..].Trim());

            return (name.Trim(), null);
        }

        private void Rebuild(ServiceOptions opts)
        {
            _prefixStrippers = CompileMany(opts.PrefixStripPatterns, RegexOptions.Compiled);
            _titleNoiseStrippers = CompileMany(opts.TitleNoisePatterns, RegexOptions.Compiled | RegexOptions.IgnoreCase);
            _featuringRegex = CompileOne(opts.FeaturingPattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
        }

        private Regex[] CompileMany(string[] patterns, RegexOptions options)
        {
            var result = new List<Regex>(patterns.Length);
            foreach (var pattern in patterns)
            {
                if (string.IsNullOrWhiteSpace(pattern))
                    continue;

                try
                {
                    result.Add(new Regex(pattern, options));
                }
                catch (ArgumentException ex)
                {
                    _logger.LogWarning(ex, "Skipping invalid filename-parsing regex: {Pattern}", pattern);
                }
            }
            return result.ToArray();
        }

        private Regex? CompileOne(string pattern, RegexOptions options)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                return null;

            try
            {
                return new Regex(pattern, options);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Skipping invalid filename-parsing regex: {Pattern}", pattern);
                return null;
            }
        }

        private void ApplyFeaturingHandling(Media media, ServiceOptions opts)
        {
            if (_featuringRegex is null || opts.FeaturingHandling == FeaturingHandling.Ignore)
                return;

            var m = _featuringRegex.Match(media.Title);
            if (!m.Success)
                return;

            var featured = m.Groups[1].Value.Trim();
            if (string.IsNullOrWhiteSpace(featured))
                return;

            var cleanedTitle = media.Title[..m.Index].TrimEnd();

            switch (opts.FeaturingHandling)
            {
                case FeaturingHandling.AppendToArtist:
                    media.Title = cleanedTitle;
                    media.Artist = $"{media.Artist} feat. {featured}";
                    break;
                case FeaturingHandling.MoveToNotes:
                    media.Title = cleanedTitle;
                    media.Notes = string.IsNullOrEmpty(media.Notes)
                        ? $"feat. {featured}"
                        : $"{media.Notes}; feat. {featured}";
                    break;
                case FeaturingHandling.StripFromTitle:
                    media.Title = cleanedTitle;
                    break;
            }
        }

        private string StripTitleNoise(string title)
        {
            bool changed;
            do
            {
                changed = false;
                foreach (var stripper in _titleNoiseStrippers)
                {
                    var next = stripper.Replace(title, "").TrimEnd();
                    if (next != title)
                    {
                        title = next;
                        changed = true;
                    }
                }
            } while (changed);

            return title;
        }

        public class ServiceOptions
        {
            public const string SectionName = nameof(MediaFileParsingService);

            public FilenameFormat Format { get; init; } = FilenameFormat.ArtistFirst;

            public string[] Separators { get; init; } = [" - ", " – ", " — "];

            public string[] PrefixStripPatterns { get; init; } =
            [
                @"^\[.*?\]\s*",
                @"^(?:Track\s+)?\d+(?:\s*[.\)]\s+|\s+-\s+)"
            ];

            public string[] TitleNoisePatterns { get; init; } =
            [
                @"\s*[\(\[]\s*karaoke(?:\s+version)?\s*[\)\]]\s*$",
                @"\s*[\(\[]\s*official(?:\s+(?:music\s+)?video)?\s*[\)\]]\s*$",
                @"\s*[\(\[]\s*hd\s*[\)\]]\s*$",
                @"\s*[\(\[]\s*lyrics?\s*[\)\]]\s*$",
                @"\s*[\(\[]\s*instrumental\s*[\)\]]\s*$"
            ];

            public string FeaturingPattern { get; init; } =
                @"\s*[\(\[]?\s*(?:feat|ft|featuring)\.?\s+(.+?)\s*[\)\]]?\s*$";

            public FeaturingHandling FeaturingHandling { get; init; } = FeaturingHandling.AppendToArtist;

            public string FallbackArtistName { get; init; } = "Unknown Artist";
        }
    }

    public enum FilenameFormat
    {
        ArtistFirst,
        TitleFirst
    }

    public enum FeaturingHandling
    {
        Ignore,
        AppendToArtist,
        MoveToNotes,
        StripFromTitle
    }
}
