namespace KHost.LrcLib.Models;

public sealed record LyricsRecord(
    long Id,
    string TrackName,
    string ArtistName,
    string? AlbumName,
    double? Duration,
    bool Instrumental,
    string? PlainLyrics,
    string? SyncedLyrics);
