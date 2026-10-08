using KHost.Abstractions.Models;

namespace KHost.Domain.Services;

/// <summary>Brings a file the host picked in the console's file picker into the library.</summary>
/// <remarks>Domain-only: a browser picker hands over bytes and a name, never a path, so the file is
/// kept as a copy in the host's media folder rather than read where it was. Images, videos and
/// audio only: karaoke arrives as a pair or an archive, which one picked file cannot be.</remarks>
public interface IMediaUploader
{
    /// <summary>The extensions a picked file may have to be added as any of <paramref name="types"/>,
    /// each lower case with a leading dot; empty when none of them can be added this way.</summary>
    IReadOnlyList<string> ExtensionsFor(IEnumerable<MediaType> types);

    /// <summary>Which of <paramref name="types"/> a file named <paramref name="fileName"/> would be
    /// added as, or null when its extension fits none of them.</summary>
    MediaType? TypeFor(string fileName, IEnumerable<MediaType> types);

    /// <summary>The largest file accepted as <paramref name="type"/>, in bytes; 0 for a type that
    /// cannot be added this way.</summary>
    long MaxBytesFor(MediaType type);

    /// <summary>Saves <paramref name="content"/> under <paramref name="fileName"/>, never over another
    /// file, and adds it to the library as <paramref name="type"/>.</summary>
    /// <returns>The library row. A file already copied in returns its existing row.</returns>
    /// <exception cref="NotSupportedException">The name does not fit <paramref name="type"/>.</exception>
    Task<Media> AddAsync(string fileName, Stream content, MediaType type, CancellationToken cancellationToken = default);
}
