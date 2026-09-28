namespace KHost.Abstractions.Models;

/// <summary>A named list of visualisations drawn under a song's words, one per song.</summary>
/// <remarks>A venue names one in <see cref="Venue.VenueSettings.VisualisationPlaylistId"/>; a venue
/// that names none shows black under the words.</remarks>
public class VisualisationPlaylist : RepositoryModel
{
    /// <summary>The name shown throughout the app.</summary>
    public required string Name { get; set; }

    /// <summary>Case- and accent-insensitive form of <see cref="Name"/>, used to match it; the host
    /// keeps this in sync, so a plugin should treat it as read-only.</summary>
    public string NameFolded { get; set; } = string.Empty;

    /// <summary>True picks an entry at random for each song, never the one just shown when there is
    /// another; false takes them in order, wrapping at the end.</summary>
    public bool Shuffle { get; set; }

    /// <summary>The visualisations, in play order.</summary>
    public List<VisualisationEntry> Entries { get; set; } = [];
}

/// <summary>Where a visualiser preset comes from.</summary>
public enum VisualiserPresetSource
{
    /// <summary>One of the presets the host ships.</summary>
    Bundled,

    /// <summary>A preset file the host imported for itself.</summary>
    Imported,
}

/// <summary>One line in a <see cref="VisualisationPlaylist"/>: a preset and how it is drawn.</summary>
/// <remarks>The same preset may appear in several entries, each tuned differently. The percentages
/// are held to their ranges on save.</remarks>
public class VisualisationEntry : RepositoryModel
{
    /// <summary>The lowest and highest <see cref="Brightness"/>.</summary>
    public const int MinBrightness = 25, MaxBrightness = 200;

    /// <summary>The lowest and highest <see cref="Saturation"/>.</summary>
    public const int MinSaturation = 0, MaxSaturation = 200;

    /// <summary>The lowest and highest <see cref="Sensitivity"/>.</summary>
    public const int MinSensitivity = 0, MaxSensitivity = 300;

    /// <summary>The playlist this entry belongs to.</summary>
    public Guid VisualisationPlaylistId { get; set; }

    /// <summary>Play order within the playlist; ignored when it shuffles.</summary>
    public int Position { get; set; }

    /// <summary>Whether <see cref="PresetName"/> names a shipped preset or an imported one.</summary>
    public VisualiserPresetSource PresetSource { get; set; }

    /// <summary>The preset's name, as <see cref="VisualiserPreset.Name"/> gives it.</summary>
    /// <remarks>A name that no longer resolves — an imported preset since deleted — draws black.</remarks>
    public string PresetName { get; set; } = string.Empty;

    /// <summary>Percent of the preset's own brightness; 100 draws it as made.</summary>
    public int Brightness { get; set; } = 100;

    /// <summary>Percent of the preset's own colour; 0 is greyscale, 100 as made.</summary>
    public int Saturation { get; set; } = 100;

    /// <summary>How hard the picture reacts to the music, in percent; 100 reacts as the preset was
    /// made to, 0 not at all.</summary>
    public int Sensitivity { get; set; } = 100;

    /// <summary>Whether each line of words is drawn on a dark band so it reads over the picture.</summary>
    public bool DarkenBehindWords { get; set; } = true;
}

/// <summary>A visualiser preset a host can put in a playlist.</summary>
public sealed class VisualiserPreset
{
    /// <summary>What an entry names it by; unique within its <see cref="Source"/>.</summary>
    public required string Name { get; init; }

    /// <summary>Shipped or imported.</summary>
    public required VisualiserPresetSource Source { get; init; }

    /// <summary>When an imported preset was last written, UTC; null for a shipped one.</summary>
    /// <remarks>A re-import under the same name moves it, so a display can tell the file changed.</remarks>
    public DateTime? ImportedUtc { get; init; }
}

/// <summary>What became of a preset import.</summary>
public sealed class VisualiserPresetImport
{
    /// <summary>The preset as stored; null when it was refused.</summary>
    public VisualiserPreset? Preset { get; init; }

    /// <summary>Why it was refused, as a line a host can act on; null when it was stored.</summary>
    public string? Error { get; init; }

    /// <summary>True when an imported preset of the same name was replaced.</summary>
    public bool Replaced { get; init; }
}
