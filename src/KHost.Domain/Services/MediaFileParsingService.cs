using System.Diagnostics;
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
        private readonly IAnalyticsService _analytics;

        private Regex[] _prefixStrippers = [];
        private Regex[] _titleNoiseStrippers = [];
        private Regex? _featuringRegex;

        public MediaFileParsingService(
            ILogger<MediaFileParsingService> logger,
            IOptionsMonitor<ServiceOptions> options,
            IAnalyticsService analytics)
        {
            _logger = logger;
            _options = options;
            _analytics = analytics;
            Rebuild(options.CurrentValue);
            options.OnChange(Rebuild);
        }

        public async Task<Media> LoadAndParseAsync(string filePath)
        {
            var opts = _options.CurrentValue;
            var (parsedTitle, parsedArtist) = GetTitleAndArtistFromFilename(filePath);

            // ffprobe has nothing to say about a still, and the host clock is what takes one down,
            // so it gets a default length instead of the null that would make it unplayable.
            var isStill = MediaFormats.IsImage(Path.GetExtension(filePath));
            var probe = isStill ? null : await TryProbeAsync(filePath);

            var media = new Media
            {
                FilePath = filePath,
                Title = probe?.Title ?? parsedTitle,
                Artist = probe?.Artist ?? parsedArtist ?? opts.FallbackArtistName,
                Duration = probe?.Duration ?? (isStill ? MediaFormats.DefaultImageDuration : null),
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
            var name = StripPrefixes(Path.GetFileNameWithoutExtension(filePath));

            return TrySeparatorParse(name, opts)
                ?? TryParentheticalParse(name)
                ?? TryUnderscoreParse(name)
                ?? (name.Trim(), null);
        }

        private string StripPrefixes(string name)
        {
            foreach (var stripper in _prefixStrippers)
                name = stripper.Replace(name, "").Trim();
            return name;
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

        private async Task<ProbeResult?> TryProbeAsync(string filePath)
        {
            var probeFilePath = Path.GetExtension(filePath).Equals(".cdg", StringComparison.OrdinalIgnoreCase)
                ? Path.ChangeExtension(filePath, ".mp3")
                : filePath;

            var sw = Stopwatch.StartNew();
            try
            {
                var probe = await FFProbe.AnalyseAsync(probeFilePath);
                var (title, artist) = ExtractProbeTags(probe.Format.Tags);
                return new ProbeResult(
                    probe.Format.Duration > TimeSpan.Zero ? probe.Format.Duration : null,
                    title,
                    artist);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "FFProbe failed for {FilePath}; falling back to filename-derived metadata", filePath);
                return null;
            }
            finally
            {
                _analytics.RecordMediaParseDuration(sw.Elapsed.TotalMilliseconds);
            }
        }

        internal static (string? title, string? artist) ExtractProbeTags(IReadOnlyDictionary<string, string>? tags)
        {
            string? probeTitle = null;
            string? probeArtist = null;
            tags?.TryGetValue("title", out probeTitle);
            tags?.TryGetValue("artist", out probeArtist);
            return (
                string.IsNullOrWhiteSpace(probeTitle) ? null : probeTitle,
                string.IsNullOrWhiteSpace(probeArtist) ? null : probeArtist);
        }

        private (string Title, string? Artist)? TrySeparatorParse(string name, ServiceOptions opts)
        {
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
            return null;
        }

        private (string Title, string? Artist)? TryParentheticalParse(string name)
        {
            var match = _trailingParenthetical.Match(name);
            if (!match.Success || string.IsNullOrWhiteSpace(match.Groups[2].Value))
                return null;
            return (match.Groups[1].Value.Trim(), match.Groups[2].Value.Trim());
        }

        private static (string Title, string? Artist)? TryUnderscoreParse(string name)
        {
            var idx = name.IndexOf('_');
            if (idx <= 0 || idx != name.LastIndexOf('_'))
                return null;
            return (name[..idx].Trim(), name[(idx + 1)..].Trim());
        }

        private void ApplyFeaturingHandling(Media media, ServiceOptions opts)
        {
            if (_featuringRegex is null || opts.FeaturingHandling == FeaturingHandling.Ignore)
                return;

            if (!TryExtractFeatured(media.Title, out var featured, out var cleanedTitle))
                return;

            ApplyFeaturedArtistToMedia(media, cleanedTitle, featured, opts.FeaturingHandling);
        }

        private bool TryExtractFeatured(string title, out string featured, out string cleanedTitle)
        {
            featured = string.Empty;
            cleanedTitle = title;

            var m = _featuringRegex!.Match(title);
            if (!m.Success)
                return false;

            var candidate = m.Groups[1].Value.Trim();
            if (string.IsNullOrWhiteSpace(candidate))
                return false;

            featured = candidate;
            cleanedTitle = title[..m.Index].TrimEnd();
            return true;
        }

        private static void ApplyFeaturedArtistToMedia(Media media, string cleanedTitle, string featured, FeaturingHandling handling)
        {
            switch (handling)
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

        private sealed record ProbeResult(TimeSpan? Duration, string? Title, string? Artist);
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
