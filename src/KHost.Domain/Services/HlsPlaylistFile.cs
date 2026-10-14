using System.Globalization;

namespace KHost.Domain.Services;

/// <summary>Reads and settles the HLS playlist an encode writes into its session directory.</summary>
internal static class HlsPlaylistFile
{
    internal const string FileName = "stream.m3u8";

    private const string EndList = "#EXT-X-ENDLIST";

    /// <summary>Adds the end marker to a playlist ffmpeg left open; true when it was added.</summary>
    /// <remarks>Replaced whole rather than appended to, so a display never reads half a line; retried,
    /// as on Windows a scanner may hold the file for a moment.</remarks>
    internal static async Task<bool> EndPlaylistAsync(string directory, int attempts = 20, int retryMilliseconds = 100)
    {
        var playlist = Path.Combine(directory, FileName);
        var pending = playlist + ".end";

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var text = await File.ReadAllTextAsync(playlist);
                if (text.Contains(EndList, StringComparison.Ordinal)) return false;

                await File.WriteAllTextAsync(pending, (text.EndsWith('\n') ? text : text + "\n") + EndList + "\n");
                File.Move(pending, playlist, overwrite: true);
                return true;
            }
            catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && attempt < attempts)
            {
                await Task.Delay(retryMilliseconds);
            }
        }
    }

    /// <summary>The duration of each segment a playlist lists, in order; -1 for one it cannot read.</summary>
    internal static List<double> ListedSegmentSeconds(string playlist) =>
    [
        .. playlist.Split('\n')
            .Where(line => line.StartsWith("#EXTINF:", StringComparison.Ordinal))
            .Select(line => double.TryParse(
                line["#EXTINF:".Length..].TrimEnd(',', '\r'), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
                ? d
                : -1)
    ];

    /// <summary>Moves a playlist .tmp ffmpeg left behind over the playlist; true when one was moved.</summary>
    /// <remarks>Retried, as whatever blocked ffmpeg's rename may still hold the file for a moment.</remarks>
    internal static async Task<bool> CompletePlaylistAsync(string directory, int attempts = 20, int retryMilliseconds = 100)
    {
        var playlist = Path.Combine(directory, FileName);
        var pending = playlist + ".tmp";

        for (var attempt = 1; ; attempt++)
        {
            if (!File.Exists(pending)) return false;

            try
            {
                File.Move(pending, playlist, overwrite: true);
                return true;
            }
            catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && attempt < attempts)
            {
                await Task.Delay(retryMilliseconds);
            }
        }
    }
}
