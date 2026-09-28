using System.Globalization;

namespace KHost.UserInterface.Models;

/// <summary>Shared so the queue panels, song controls and performance history can't drift apart.</summary>
public static class SongAdjustmentDisplay
{
    public static string FormatPitch(int semitones) =>
        semitones.ToString("+#;−#;0", CultureInfo.InvariantCulture);

    public static string FormatTempo(int tempo) =>
        tempo.ToString("+#;−#;0", CultureInfo.InvariantCulture) + "%";
}
