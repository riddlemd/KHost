namespace KHost.Abstractions.Models;

/// <summary>A new level for one voice against the music, for a display mixing the stems itself.</summary>
/// <remarks>Handed to <see cref="KHost.Abstractions.Services.IDisplayProvider.SetStemVolumeAsync"/>.
/// By role rather than by index, because that is how a host asks for it.</remarks>
public sealed class StemLevel
{
    /// <summary>Which voice this changes the level of.</summary>
    public required AudioTrackRole Role { get; init; }

    /// <summary>Gain against the music, 0-100; the music track itself has no level of its own.</summary>
    public required int Volume { get; init; }
}
