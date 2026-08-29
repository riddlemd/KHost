using KHost.Abstractions.Models;
using System.Text.Json.Serialization;

namespace KHost.Abstractions.Services.IPC;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(LoadMediaCommand), "loadMedia")]
[JsonDerivedType(typeof(PlayCommand), "play")]
[JsonDerivedType(typeof(PauseCommand), "pause")]
[JsonDerivedType(typeof(StopCommand), "stop")]
[JsonDerivedType(typeof(SeekCommand), "seek")]
[JsonDerivedType(typeof(SetVolumeCommand), "setVolume")]
[JsonDerivedType(typeof(SetTimelineCommand), "setTimeline")]
[JsonDerivedType(typeof(SetVideoCommand), "setVideo")]
[JsonDerivedType(typeof(LoadBackgroundCommand), "loadBackground")]
[JsonDerivedType(typeof(PlayBackgroundCommand), "playBackground")]
[JsonDerivedType(typeof(PauseBackgroundCommand), "pauseBackground")]
[JsonDerivedType(typeof(StopBackgroundCommand), "stopBackground")]
[JsonDerivedType(typeof(SetBackgroundVolumeCommand), "setBackgroundVolume")]
[JsonDerivedType(typeof(ShowImageCommand), "showImage")]
[JsonDerivedType(typeof(HideImageCommand), "hideImage")]
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

    /// <summary>
    /// Tempo percentage the stream was transcoded at. The screen's own clock runs in stream
    /// seconds, so every position crossing this boundary has to be scaled by it.
    /// </summary>
    public int Tempo { get; init; }
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

/// <summary>
/// Blanks the picture without stopping playback — a screen driving speakers in another room has
/// no reason to render, and a blanked one still has to stay on the group timeline.
/// </summary>
public sealed class SetVideoCommand : ScreenCommandBase
{
    public required bool Enabled { get; init; }
}

/// <summary>
/// The second audio channel, for break music and an ad's own bed. Deliberately thin next to the
/// song commands: it carries no timeline and is never corrected, because only the screen the room
/// hears is given any of it — there is nothing for it to stay in step with.
/// </summary>
public sealed class LoadBackgroundCommand : ScreenCommandBase
{
    public required string StreamUrl { get; init; }

    /// <summary>Starts as soon as it can play, sparing the caller a second round trip.</summary>
    public bool AutoPlay { get; init; } = true;
}

public sealed class PlayBackgroundCommand : ScreenCommandBase { }
public sealed class PauseBackgroundCommand : ScreenCommandBase { }

public sealed class StopBackgroundCommand : ScreenCommandBase
{
    public TimeSpan? FadeDuration { get; init; }
}

/// <summary>
/// Separate from <see cref="SetVolumeCommand"/>: a bed sits under the room at its own level, and
/// the song's volume is the host's fader.
/// </summary>
public sealed class SetBackgroundVolumeCommand : ScreenCommandBase
{
    public required float Volume { get; init; }
}

/// <summary>
/// Puts a still on screen — an image ad, or the venue's own card while nothing is playing. It
/// carries no duration because there is nothing here to time: no transcode is opened and no
/// element is playing, so the host's clock is the only thing that decides when it comes down.
/// </summary>
public sealed class ShowImageCommand : ScreenCommandBase
{
    public required string Url { get; init; }

    /// <summary>Sent with the picture: the screen holds no library to look it up in.</summary>
    public ImageScaling Scaling { get; init; }
}

public sealed class HideImageCommand : ScreenCommandBase { }

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(ScreenPlaybackState), "playback")]
[JsonDerivedType(typeof(ScreenBackgroundState), "background")]
public abstract class ScreenStateBase : IScreenState { }

/// <summary>
/// Sent when the background track ends or stops, which is how the host learns to pick the next
/// one. The song's position clock must not see any of this — reporting it as playback state
/// would run the singer's performance to completion off the wrong channel.
/// </summary>
public sealed class ScreenBackgroundState : ScreenStateBase
{
    public required string? StreamUrl { get; init; }
    public required bool IsPlaying { get; init; }

    /// <summary>True exactly once per track, when it played out on its own.</summary>
    public required bool HasEnded { get; init; }
}

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
