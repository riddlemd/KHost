using System.Text.Json.Serialization;

namespace KHost.Abstractions.Services.IPC;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(LoadMediaCommand), "loadMedia")]
[JsonDerivedType(typeof(PlayCommand), "play")]
[JsonDerivedType(typeof(PauseCommand), "pause")]
[JsonDerivedType(typeof(StopCommand), "stop")]
[JsonDerivedType(typeof(SeekCommand), "seek")]
[JsonDerivedType(typeof(SetVolumeCommand), "setVolume")]
[JsonDerivedType(typeof(SetPitchCommand), "setPitch")]
[JsonDerivedType(typeof(SetTimelineCommand), "setTimeline")]
[JsonDerivedType(typeof(SetVideoCommand), "setVideo")]
public abstract class ScreenCommandBase : IScreenCommand { }

/// <summary>
/// Where the song should be, against the host's clock rather than "now" — screens receive a
/// command at different moments and take different times to act on it. Sync-capable screens only.
/// </summary>
public sealed class SetTimelineCommand : ScreenCommandBase
{
    /// <summary>Song position that <see cref="AnchorUtc"/> corresponds to.</summary>
    public required TimeSpan Position { get; init; }

    /// <summary>May be slightly ahead, giving every screen one instant to start on.</summary>
    public required DateTime AnchorUtc { get; init; }

    /// <summary>When false the timeline is frozen at <see cref="Position"/> and does not advance.</summary>
    public required bool IsPlaying { get; init; }

    /// <summary>Defines the timeline rather than chasing it, so it is never corrected.</summary>
    public bool IsPrimary { get; init; }
}

public sealed class LoadMediaCommand : ScreenCommandBase
{
    /// <summary>
    /// The host transcodes once and every screen plays that stream, so there is no file path here:
    /// no screen has a decoder, and none can reach the host's filesystem.
    /// </summary>
    public required string StreamUrl { get; init; }

    /// <summary>Song position the stream's zero maps to; add it before reporting a position.</summary>
    public TimeSpan StreamStartOffset { get; init; }
}

public sealed class PlayCommand : ScreenCommandBase { }
public sealed class PauseCommand : ScreenCommandBase { }

public sealed class StopCommand : ScreenCommandBase
{
    public TimeSpan? FadeDuration { get; init; }
}

public sealed class SeekCommand : ScreenCommandBase
{
    public required TimeSpan Position { get; init; }
}

public sealed class SetVolumeCommand : ScreenCommandBase
{
    public required float Volume { get; init; }
}

public sealed class SetPitchCommand : ScreenCommandBase
{
    public required int Semitones { get; init; }
}

/// <summary>
/// Blanks the picture without stopping playback — a screen driving speakers in another room has
/// no reason to render, and a blanked one still has to stay on the group timeline.
/// </summary>
public sealed class SetVideoCommand : ScreenCommandBase
{
    public required bool Enabled { get; init; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(ScreenPlaybackState), "playback")]
public abstract class ScreenStateBase : IScreenState { }

public sealed class ScreenPlaybackState : ScreenStateBase
{
    /// <summary>The stream the screen is playing, not a file — a screen opens nothing local.</summary>
    public required string? StreamUrl { get; init; }
    public required bool IsPlaying { get; init; }
    public required TimeSpan Position { get; init; }
    public required TimeSpan Duration { get; init; }

    /// <summary>
    /// Sample time in host clock, via the screen's measured offset. Guessing the delivery latency
    /// instead would bias the timeline permanently. Null before an offset is established.
    /// </summary>
    public DateTime? SampledAtUtc { get; init; }
}
