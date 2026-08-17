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
public abstract class ScreenCommandBase : IScreenCommand { }

/// <summary>
/// Where the song should be, expressed against the host's clock rather than "now" — the only way
/// several screens can agree, since each one receives a command at a different moment and takes a
/// different time to act on it. Sent only to screens that declared
/// <see cref="ScreenCapabilities.SupportsSync"/>.
/// </summary>
public sealed class SetTimelineCommand : ScreenCommandBase
{
    /// <summary>Song position that <see cref="AnchorUtc"/> corresponds to.</summary>
    public required TimeSpan Position { get; init; }

    /// <summary>
    /// Host UTC at which <see cref="Position"/> is the correct position. May be slightly in the
    /// future, giving every screen a common instant to start on rather than starting on arrival.
    /// </summary>
    public required DateTime AnchorUtc { get; init; }

    /// <summary>When false the timeline is frozen at <see cref="Position"/> and does not advance.</summary>
    public required bool IsPlaying { get; init; }

    /// <summary>
    /// The timing reference defines the timeline instead of chasing it, so it is never corrected.
    /// Exactly one screen holds this; the rest converge onto what it actually plays. It is the
    /// audio screen wherever that screen can sync, because correcting a screen means seeking it
    /// and seeking the one the room hears is an audible glitch.
    /// </summary>
    public bool IsTimingReference { get; init; }
}

public sealed class LoadMediaCommand : ScreenCommandBase
{
    /// <summary>
    /// Only usable by a screen that shares a filesystem with the host — which is why
    /// <see cref="StreamUrl"/> exists. KHost.Screen (Avalonia) still decodes from this.
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Host-served HLS playlist. Screens that can consume it should prefer it: it needs no access
    /// to the media file and no local transcode. Null when the host has no stream to offer.
    /// </summary>
    public string? StreamUrl { get; init; }

    /// <summary>
    /// Song position that <see cref="StreamUrl"/>'s own zero maps to. Non-zero after a pitch
    /// change, which restarts the transcode part-way through; a consumer must add it before
    /// reporting an absolute position.
    /// </summary>
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

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(ScreenPlaybackState), "playback")]
public abstract class ScreenStateBase : IScreenState { }

public sealed class ScreenPlaybackState : ScreenStateBase
{
    public required string? LoadedFilePath { get; init; }
    public required bool IsPlaying { get; init; }
    public required TimeSpan Position { get; init; }
    public required TimeSpan Duration { get; init; }

    /// <summary>
    /// Host-clock instant <see cref="Position"/> was sampled at, translated through the screen's
    /// measured clock offset. Without it the host would have to guess the delivery latency, and
    /// that guess becomes a permanent bias in the timeline built from this report. Null from a
    /// screen that has not established a clock offset.
    /// </summary>
    public DateTime? SampledAtUtc { get; init; }
}
