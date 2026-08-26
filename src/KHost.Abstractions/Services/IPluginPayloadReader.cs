using KHost.Abstractions.Models.Plugins;

namespace KHost.Abstractions.Services;

/// <summary>
/// Turns a downloaded plugin zip into files on disk, or refuses it. One call rather than an
/// extract-then-check pair, so a caller cannot unpack a payload and forget to validate it.
/// </summary>
public interface IPluginPayloadReader
{
    /// <summary>
    /// Extracts into <paramref name="destination"/>, or throws with the reason. Every entry is
    /// checked before a byte is written, so a refused archive leaves nothing behind. Null
    /// <paramref name="expectedId"/> takes the payload's word, which is how a catalog entry
    /// learns an id rather than asserting one.
    /// </summary>
    PluginPayloadContents Unpack(string zipPath, string destination, Guid? expectedId = null);
}
