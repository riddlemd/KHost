using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

/// <summary>The visualiser presets a host can put in a playlist: the host's own drawings, the presets
/// it ships, and those it imported for itself.</summary>
/// <remarks>Host-owned; a plugin has nothing to implement. An imported preset is a butterchurn
/// preset file, and runs as code on whatever draws it, so importing one is trusting it. Every import
/// and delete announces <see cref="KHost.Abstractions.Messaging.Messages.VisualiserPresetsChanged"/>.
/// </remarks>
public interface IVisualiserPresetService
{
    /// <summary>Every preset on offer: the built-in drawings, the shipped presets, then the imported
    /// ones by name.</summary>
    IReadOnlyList<VisualiserPreset> ReadAll();

    /// <summary>Stores a preset file under a name taken from <paramref name="fileName"/>, replacing
    /// an imported preset of that name.</summary>
    /// <returns>The stored preset, or why it was refused: not a butterchurn preset, or too large.
    /// Never throws for a bad file.</returns>
    Task<VisualiserPresetImport> ImportAsync(string fileName, Stream content, CancellationToken cancellationToken = default);

    /// <summary>The imported preset's file as stored; null when no imported preset has that name.</summary>
    Task<string?> ReadImportedAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Deletes an imported preset. An entry naming it draws black from then on.</summary>
    /// <returns>False when there was none by that name.</returns>
    bool DeleteImported(string name);
}
