using KHost.Abstractions.Models.Plugins;

namespace KHost.Abstractions.Services;

/// <summary>
/// Turns a downloaded plugin zip into files on disk, or refuses it. One call rather than an
/// extract-then-check pair, so a caller cannot unpack a payload and forget to validate it.
/// </summary>
public interface IPluginPayloadReader
{
    /// <summary>
    /// Extracts into <paramref name="destination"/> and returns what the payload declares. Every
    /// entry is checked before a byte is written, so a rejected archive leaves nothing behind.
    /// <paramref name="expectedId"/> is the id the caller believes this is; null takes the
    /// payload's word, which is how a catalog entry learns an id rather than asserting one.
    /// Throws <see cref="InvalidOperationException"/> with the reason when the payload is refused.
    /// </summary>
    PluginPayloadContents Unpack(string zipPath, string destination, Guid? expectedId = null);
}
