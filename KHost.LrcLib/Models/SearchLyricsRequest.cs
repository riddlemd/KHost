namespace KHost.LrcLib.Models;

public sealed record SearchLyricsRequest(
    string? Query = null,
    string? TrackName = null,
    string? ArtistName = null,
    string? AlbumName = null);
