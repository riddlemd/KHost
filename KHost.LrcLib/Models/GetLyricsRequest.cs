namespace KHost.LrcLib.Models;

public sealed record GetLyricsRequest(
    string TrackName,
    string ArtistName,
    string? AlbumName = null,
    double? Duration = null);
