namespace KHost.Abstractions.Models;

/// <summary>What a visualisation draws and how it is tuned, whether it is a playlist's entry or one
/// performance's own background.</summary>
/// <remarks>The ranges and defaults are <see cref="VisualisationEntry"/>'s constants; the host holds
/// every value to them on save.</remarks>
public interface IVisualisationLook
{
    /// <summary>What is drawn: a shipped or imported preset or a built-in, named by
    /// <see cref="PresetName"/>, or a library video, named by <see cref="VideoMediaId"/>.</summary>
    VisualiserPresetSource PresetSource { get; set; }

    /// <summary>The preset's name, as <see cref="VisualiserPreset.Name"/> gives it; empty for a video.</summary>
    string PresetName { get; set; }

    /// <summary>The library video a <see cref="VisualiserPresetSource.Video"/> look draws; null for any
    /// other source.</summary>
    Guid? VideoMediaId { get; set; }

    /// <summary>Percent of the preset's own brightness, <see cref="VisualisationEntry.MinBrightness"/> to
    /// <see cref="VisualisationEntry.MaxBrightness"/>; 100 draws it as made.</summary>
    int Brightness { get; set; }

    /// <summary>Percent of the preset's own colour, <see cref="VisualisationEntry.MinSaturation"/> to
    /// <see cref="VisualisationEntry.MaxSaturation"/>; 0 is greyscale.</summary>
    int Saturation { get; set; }

    /// <summary>How hard the picture reacts to the music in percent,
    /// <see cref="VisualisationEntry.MinSensitivity"/> to <see cref="VisualisationEntry.MaxSensitivity"/>.</summary>
    int Sensitivity { get; set; }

    /// <summary>How many bars a built-in bar style draws, one of <see cref="VisualisationEntry.BarCounts"/>.</summary>
    int BarCount { get; set; }

    /// <summary>How a built-in visualisation is coloured.</summary>
    VisualiserColourScheme ColourScheme { get; set; }

    /// <summary>The colour for <see cref="VisualiserColourScheme.Single"/>, as <c>#rrggbb</c>.</summary>
    string Colour { get; set; }

    /// <summary>Whether a built-in draws in the venue's theme colours, in place of
    /// <see cref="ColourScheme"/>, at a venue that has a theme. A preset or a video draws as it is.</summary>
    bool RespectsVenueTheme { get; set; }
}
