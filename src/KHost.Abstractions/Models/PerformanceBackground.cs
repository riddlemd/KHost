namespace KHost.Abstractions.Models;

/// <summary>What one performance draws under its words in place of the venue's visualisation
/// playlist.</summary>
/// <remarks>Applies only where a visualiser would be drawn at all: a song with timed words and no
/// picture of its own, on a display that draws its own words. A song's own picture is never
/// replaced. A <see cref="PerformanceBackgroundType.Look"/> that no longer resolves (an imported
/// preset since deleted, a video row gone or not a video) draws the venue's playlist instead.</remarks>
public sealed class PerformanceBackground : IVisualisationLook
{
    /// <summary>Black, or the look the remaining settings describe.</summary>
    public PerformanceBackgroundType Type { get; set; }

    /// <inheritdoc/>
    /// <remarks>Ignored unless <see cref="Type"/> is <see cref="PerformanceBackgroundType.Look"/>.</remarks>
    public VisualiserPresetSource PresetSource { get; set; }

    /// <inheritdoc/>
    public string PresetName { get; set; } = string.Empty;

    /// <inheritdoc/>
    public Guid? VideoMediaId { get; set; }

    /// <inheritdoc/>
    public int Brightness { get; set; } = 100;

    /// <inheritdoc/>
    public int Saturation { get; set; } = 100;

    /// <inheritdoc/>
    public int Sensitivity { get; set; } = 100;

    /// <inheritdoc/>
    public int BarCount { get; set; } = VisualisationEntry.DefaultBarCount;

    /// <inheritdoc/>
    public VisualiserColourScheme ColourScheme { get; set; }

    /// <inheritdoc/>
    public string Colour { get; set; } = VisualisationEntry.DefaultColour;

    /// <inheritdoc/>
    /// <remarks>On for a new background.</remarks>
    public bool RespectsVenueTheme { get; set; } = true;
}

/// <summary>What a <see cref="PerformanceBackground"/> draws.</summary>
public enum PerformanceBackgroundType
{
    /// <summary>Nothing: black under the words, whatever the venue's playlist holds.</summary>
    Black = 0,

    /// <summary>The preset, built-in or video the background's settings name.</summary>
    Look = 1,
}
