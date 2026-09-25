namespace KHost.Abstractions.Models;

/// <summary>One song or clip for a display to load, ready to play but not playing.</summary>
/// <remarks>Handed to <see cref="KHost.Abstractions.Services.IDisplayProvider.LoadAsync"/>. At least
/// one of <see cref="StreamUrl"/> and <see cref="Stems"/> is always set.</remarks>
public sealed class DisplayLoad
{
    /// <summary>What to play end to end, already encoded by the host; null when nothing was encoded
    /// and <see cref="Stems"/> are the only way the song comes out.</summary>
    /// <remarks>Null only for a display whose
    /// <see cref="KHost.Abstractions.Services.IDisplayProvider.DescribeTarget"/> answered
    /// <see cref="RenderTarget.MixesStems"/>.</remarks>
    public string? StreamUrl { get; init; }

    /// <summary>The song position the stream's zero maps to; add it to every position reported
    /// back.</summary>
    public TimeSpan StartOffset { get; init; }

    /// <summary>Percent either side of recorded speed the stream was retimed by; scales the device's
    /// seconds back to song seconds.</summary>
    public int Tempo { get; init; }

    /// <summary>The song's parts, unmixed, for a display that mixes them itself; empty when the
    /// host already mixed.</summary>
    /// <remarks>Offered only to a display whose
    /// <see cref="KHost.Abstractions.Services.IDisplayProvider.DescribeTarget"/> answered
    /// <see cref="RenderTarget.MixesStems"/>. <see cref="StreamUrl"/> may be set beside them.</remarks>
    public IReadOnlyList<StemSource> Stems { get; init; } = [];
}
