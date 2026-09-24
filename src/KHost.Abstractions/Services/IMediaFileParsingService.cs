using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

/// <summary>Works out what a media file is called, who it is by and how long it runs, before it
/// becomes a library row.</summary>
/// <remarks>Host-owned; a plugin may take it but has nothing to implement. A plugin whose container
/// the host cannot read makes its files describable by implementing <see cref="IMediaProbe"/>,
/// which this asks first. How file names are split into title and artist is the host's setting. A
/// host singleton, callable from any thread. Announces nothing, and saves nothing.</remarks>
public interface IMediaFileParsingService
{
    /// <summary>Type decides which fields apply; only a performance has a performer.</summary>
    /// <returns>An unsaved <see cref="Media"/> marked Ready. Tags in the file win over the name;
    /// a file that cannot be read still comes back, described from its name alone. Only karaoke and
    /// audio get a placeholder artist when none is found; a still gets a default duration.</returns>
    Task<Media> LoadAndParseAsync(string filePath, MediaType type = MediaType.Karaoke);

    /// <summary>Splits a file name into title and artist, without opening the file.</summary>
    /// <returns>The artist is null when the name carries none; the title is never null.</returns>
    (string Title, string? Artist) GetTitleAndArtistFromFilename(string filePath);
}
