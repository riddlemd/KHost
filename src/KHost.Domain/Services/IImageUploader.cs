using KHost.Abstractions.Models;

namespace KHost.Domain.Services;

/// <summary>Brings a picture the host picked in the console's file picker into the library.</summary>
/// <remarks>Domain-only: a browser picker hands over bytes and a name, never a path, so the picture
/// is kept as a copy in the host's media folder rather than read where it was.</remarks>
public interface IImageUploader
{
    /// <summary>The largest picture accepted, in bytes.</summary>
    long MaxBytes { get; }

    /// <summary>Saves <paramref name="content"/> as a still named after <paramref name="fileName"/>,
    /// never over another file, and adds it to the library as an image.</summary>
    /// <returns>The library row. Picking a picture already copied in returns its existing row.</returns>
    /// <exception cref="NotSupportedException">The name is not a picture the screen can show.</exception>
    Task<Media> AddAsync(string fileName, Stream content, CancellationToken cancellationToken = default);
}
