namespace KHost.Abstractions.Models;

/// <summary>A stream for a display's second audio channel: break music, or an ad's own voiceover.</summary>
/// <remarks>Handed to <see cref="KHost.Abstractions.Services.IDisplayProvider.LoadBackgroundAsync"/>.
/// The channel carries no song position, so nothing here maps to one.</remarks>
public sealed class BackgroundLoad
{
    /// <summary>Where the display fetches the audio.</summary>
    public required string StreamUrl { get; init; }

    /// <summary>Whether to start as soon as it can play, rather than waiting for
    /// <see cref="KHost.Abstractions.Services.IDisplayProvider.PlayBackgroundAsync"/>.</summary>
    public bool AutoPlay { get; init; } = true;
}
