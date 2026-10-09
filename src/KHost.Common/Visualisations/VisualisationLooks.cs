using KHost.Abstractions.Models;

namespace KHost.Common.Visualisations;

/// <summary>Holds a visualisation's look to what a display can draw, the same for a playlist entry
/// and for a performance's own background.</summary>
public static class VisualisationLooks
{
    /// <summary>Copies every look setting from <paramref name="source"/> onto <paramref name="target"/>,
    /// each inside its range: a preset name only for a preset, a video only for a video, the
    /// percentages clamped, the bar count the nearest on offer, an unknown palette as classic and a
    /// colour that is not <c>#rrggbb</c> as <see cref="VisualisationEntry.DefaultColour"/>.</summary>
    public static void CopyWithinRangesTo(this IVisualisationLook source, IVisualisationLook target)
    {
        var video = source.PresetSource == VisualiserPresetSource.Video;

        target.PresetSource = source.PresetSource;
        target.PresetName = video ? string.Empty : source.PresetName ?? string.Empty;
        target.VideoMediaId = video ? source.VideoMediaId : null;
        target.Brightness = Math.Clamp(source.Brightness, VisualisationEntry.MinBrightness, VisualisationEntry.MaxBrightness);
        target.Saturation = Math.Clamp(source.Saturation, VisualisationEntry.MinSaturation, VisualisationEntry.MaxSaturation);
        target.Sensitivity = Math.Clamp(source.Sensitivity, VisualisationEntry.MinSensitivity, VisualisationEntry.MaxSensitivity);
        target.BarCount = VisualisationEntry.BarCounts.MinBy(count => Math.Abs(count - source.BarCount));
        target.ColourScheme = Enum.IsDefined(source.ColourScheme) ? source.ColourScheme : VisualiserColourScheme.Classic;
        target.Colour = IsColour(source.Colour) ? source.Colour.ToLowerInvariant() : VisualisationEntry.DefaultColour;
    }

    /// <summary>A copy of <paramref name="background"/> held to its ranges: a look as
    /// <see cref="CopyWithinRangesTo"/> holds it, black with every look setting at its default.
    /// Null, or a type this build does not know, is null: the venue's playlist.</summary>
    public static PerformanceBackground? BackgroundWithinRanges(PerformanceBackground? background)
    {
        if (background is null || !Enum.IsDefined(background.Type)) return null;

        var held = new PerformanceBackground { Type = background.Type };
        if (background.Type == PerformanceBackgroundType.Look) background.CopyWithinRangesTo(held);

        return held;
    }

    /// <summary>Whether a colour is <c>#rrggbb</c>, the one form a colour input and the screen share.</summary>
    private static bool IsColour(string? colour)
        => colour is { Length: 7 } && colour[0] == '#' && colour.Skip(1).All(Uri.IsHexDigit);
}
