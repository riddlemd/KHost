namespace KHost.Abstractions.Models;

/// <summary>A named list of visualisations drawn under a song's words, one per song.</summary>
/// <remarks>A venue names one in <see cref="Venue.VenueSettings.VisualisationPlaylistId"/>; a venue
/// that names none shows black under the words.</remarks>
public class VisualisationPlaylist : RepositoryModel
{
    /// <summary>Id of the built-in "Basic Backgrounds" playlist, the one a new venue starts on: the
    /// ambient scenes drawn with shapes, in the order the page lists them. Every install has this
    /// row — a migration seeds it and the host restores it if it is ever gone — and it cannot be
    /// deleted, though it can be renamed, shuffled and its entries edited like any other.</summary>
    public static readonly Guid DefaultId = new("00000000-0000-0000-0000-000000000001");

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

/// <summary>Where a visualiser preset comes from, or that an entry draws a video instead.</summary>
public enum VisualiserPresetSource
{
    /// <summary>One of the presets the host ships.</summary>
    Bundled = 0,

    /// <summary>A preset file the host imported for itself.</summary>
    Imported = 1,

    /// <summary>One of the host's own drawings (a spectrum analyser, meters), drawn without a
    /// preset; <see cref="VisualisationEntry.BarCount"/> and the colour settings apply to these only.</summary>
    BuiltIn = 2,

    /// <summary>A video from the library, named by <see cref="VisualisationEntry.VideoMediaId"/>, drawn
    /// muted and looping; the bar count, colour and sensitivity settings do not apply.</summary>
    Video = 3,
}

/// <summary>How a built-in visualisation is coloured.</summary>
public enum VisualiserColourScheme
{
    /// <summary>Green through yellow to red as a level rises, as a hi-fi's meters are.</summary>
    Classic,

    /// <summary>The screen's own accent, the colour sung words take when a timing sets none.</summary>
    Theme,

    /// <summary><see cref="VisualisationEntry.Colour"/> throughout.</summary>
    Single,
}

/// <summary>One line in a <see cref="VisualisationPlaylist"/>: a preset and how it is drawn.</summary>
/// <remarks>The same preset may appear in several entries, each tuned differently. The percentages
/// are held to their ranges on save.</remarks>
public class VisualisationEntry : RepositoryModel, IVisualisationLook
{
    /// <summary>The lowest and highest <see cref="Brightness"/>.</summary>
    public const int MinBrightness = 25, MaxBrightness = 200;

    /// <summary>The lowest and highest <see cref="Saturation"/>.</summary>
    public const int MinSaturation = 0, MaxSaturation = 200;

    /// <summary>The lowest and highest <see cref="Sensitivity"/>.</summary>
    public const int MinSensitivity = 0, MaxSensitivity = 300;

    /// <summary>The <see cref="BarCount"/> an entry starts with.</summary>
    public const int DefaultBarCount = 32;

    /// <summary>The <see cref="Colour"/> an entry starts with.</summary>
    public const string DefaultColour = "#33ccff";

    /// <summary>The bar counts on offer; any other is taken as the nearest on save.</summary>
    public static readonly IReadOnlyList<int> BarCounts = [16, 32, 64];

    /// <summary>The playlist this entry belongs to.</summary>
    public Guid VisualisationPlaylistId { get; set; }

    /// <summary>Play order within the playlist; ignored when it shuffles.</summary>
    public int Position { get; set; }

    /// <summary>What the entry draws: a shipped or imported preset or a built-in, named by
    /// <see cref="PresetName"/>, or a library video, named by <see cref="VideoMediaId"/>.</summary>
    public VisualiserPresetSource PresetSource { get; set; }

    /// <summary>The preset's name, as <see cref="VisualiserPreset.Name"/> gives it.</summary>
    /// <remarks>A name that no longer resolves — an imported preset since deleted — draws black.</remarks>
    public string PresetName { get; set; } = string.Empty;

    /// <summary>The library video a <see cref="VisualiserPresetSource.Video"/> entry draws; null for any
    /// other source.</summary>
    /// <remarks>A video since removed from the library, or one a display cannot play, draws black.</remarks>
    public Guid? VideoMediaId { get; set; }

    /// <summary>Percent of the preset's own brightness; 100 draws it as made.</summary>
    public int Brightness { get; set; } = 100;

    /// <summary>Percent of the preset's own colour; 0 is greyscale, 100 as made.</summary>
    public int Saturation { get; set; } = 100;

    /// <summary>How hard the picture reacts to the music, in percent; 100 reacts as the preset was
    /// made to, 0 not at all.</summary>
    public int Sensitivity { get; set; } = 100;

    /// <summary>How many bars a built-in bar style draws, one of <see cref="BarCounts"/>.</summary>
    public int BarCount { get; set; } = DefaultBarCount;

    /// <summary>How a built-in visualisation is coloured.</summary>
    public VisualiserColourScheme ColourScheme { get; set; }

    /// <summary>The colour for <see cref="VisualiserColourScheme.Single"/>, as <c>#rrggbb</c>;
    /// anything else is taken as <see cref="DefaultColour"/> on save.</summary>
    public string Colour { get; set; } = DefaultColour;
}

/// <summary>A visualiser preset a host can put in a playlist.</summary>
public sealed class VisualiserPreset
{
    /// <summary>What an entry names it by; unique within its <see cref="Source"/>.</summary>
    public required string Name { get; init; }

    /// <summary>Built in, shipped or imported.</summary>
    public required VisualiserPresetSource Source { get; init; }

    /// <summary>What a host is shown where it differs from <see cref="Name"/>; null shows the name.</summary>
    public string? Title { get; init; }

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
