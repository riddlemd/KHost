using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.LrcLib;
using KHost.LrcLib.Models;
using Microsoft.Extensions.Logging;

namespace KHost.Domain.Services
{
    public class LyricsService : BaseService, ILyricsService
    {
        private readonly ILrcLibClient _lrcLibClient;
        private readonly IFlashService _flash;

        public LyricsService(ILogger<LyricsService> logger, ILrcLibClient lrcLibClient, IFlashService flash)
            : base(logger)
        {
            _lrcLibClient = lrcLibClient;
            _flash = flash;
        }

        public async Task<Lyrics?> SearchAsync(string query, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(query))
                return null;

            try
            {
                var results = await _lrcLibClient.SearchAsync(new SearchLyricsRequest() { Query = query }, cancellationToken);

                if (results.Count <= 0)
                    return null;

                return MapLyricsResult(results[0]);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Null alone leaves the dialog saying no lyrics exist, when LRCLIB may never have answered.
                Logger.LogWarning(ex, "Lyrics lookup failed for '{Query}'", query);
                _flash.Show(NetworkFailureText.Describe("LRCLIB", ex, cancellationToken), FlashType.Warning);
                return null;
            }
        }

        private static Lyrics MapLyricsResult(LyricsRecord? record)
        {
            var track = record?.TrackName ?? "Unknown";
            var artist = record?.ArtistName ?? "Unknown";
            return new Lyrics
            {
                Name = $"{track} - {artist}".Trim(),
                Text = record?.PlainLyrics ?? "",
                ProviderName = "LRCLIB.NET",
                ProviderUrl = "https://lrclib.net"
            };
        }
    }
}
