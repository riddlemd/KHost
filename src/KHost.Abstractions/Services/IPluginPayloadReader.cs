using KHost.Abstractions.Models.Plugins;

namespace KHost.Abstractions.Services;

/// <summary>Turns a plugin zip into files on disk, or refuses it: one call, validation included.</summary>
/// <remarks>Host-only: the one set of rules a release must satisfy, shared by the installer and the
/// catalog tool so the two never drift. A plugin has no business with it. Stateless and callable
/// from any thread.</remarks>
public interface IPluginPayloadReader
{
    /// <summary>Unpacks <paramref name="zipPath"/> into <paramref name="destination"/> and validates
    /// what it declares.</summary>
    /// <param name="zipPath">The release archive to unpack.</param>
    /// <param name="destination">The folder to unpack into; created if missing.</param>
    /// <param name="expectedId">When given, the manifest must declare this plugin id.</param>
    /// <returns>Where the manifest landed — the destination itself, or the one folder the zip wrapped
    /// everything in — and the manifest read from it.</returns>
    /// <remarks>An archive that would write outside <paramref name="destination"/> or expand past the
    /// host's limit is refused before anything is written. The manifest is checked after
    /// extraction, so a refusal at that stage leaves the extracted files for the caller to
    /// delete.</remarks>
    /// <exception cref="InvalidOperationException">The payload was refused: it escapes its folder, is
    /// too large, has no manifest, declares a different id, targets another plugin API version, or
    /// names an entry assembly that is missing or outside the plugin folder. The message says which,
    /// in words a host can read.</exception>
    PluginPayloadContents Unpack(string zipPath, string destination, Guid? expectedId = null);
}
