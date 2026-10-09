namespace KHost.UserInterface.Endpoints;

/// <summary>Reads a playlist ffmpeg is still rewriting.</summary>
/// <remarks>
/// An EVENT playlist is rewritten as <c>stream.m3u8.tmp</c> and renamed over the live one after each
/// segment. On Windows the rename fails against a reader that does not share delete, and a read that
/// lands mid-rename meets a sharing violation, a delete-pending file (access denied) or no file at all.
/// </remarks>
internal static class LivePlaylistReader
{
    public const int DefaultAttempts = 5;

    public static readonly TimeSpan DefaultDelay = TimeSpan.FromMilliseconds(40);

    /// <summary>The playlist's text, or null when it could not be read within the attempts.</summary>
    public static async Task<string?> ReadAsync(
        string path,
        int attempts = DefaultAttempts,
        TimeSpan? delay = null,
        CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                // Sharing delete lets ffmpeg's rename replace the file while it is open here.
                await using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                return await reader.ReadToEndAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt >= attempts) return null;
            }

            await Task.Delay(delay ?? DefaultDelay, cancellationToken);
        }
    }
}
