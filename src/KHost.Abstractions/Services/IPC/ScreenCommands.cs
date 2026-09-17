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
[JsonDerivedType(typeof(SetMarqueeCommand), "setMarquee")]
[JsonDerivedType(typeof(SetScreenQrCodesCommand), "setQrCodes")]
[JsonDerivedType(typeof(SetBreakMusicCardCommand), "setBreakMusicCard")]
public abstract class ScreenCommandBase : IScreenCommand { }

/// <summary>Song position against the host's clock, not "now"; sync-capable screens only.</summary>
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
    /// <summary>The host transcodes once; every screen plays the stream, with no decoder.</summary>
    public required string StreamUrl { get; init; }

    /// <summary>Song position the stream's zero maps to; add it before reporting a position.</summary>
    public TimeSpan StreamStartOffset { get; init; }

    /// <summary>Tempo percent the stream was transcoded at; scales every position crossing it.</summary>
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

/// <summary>Blanks the picture without stopping playback; still on the group timeline.</summary>
public sealed class SetVideoCommand : ScreenCommandBase
{
    public required bool Enabled { get; init; }
}

/// <summary>Second audio channel for break music and an ad's bed; no timeline, never corrected.</summary>
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

/// <summary>Separate from <see cref="SetVolumeCommand"/>: a bed sits under the song's fader.</summary>
public sealed class SetBackgroundVolumeCommand : ScreenCommandBase
{
    public required float Volume { get; init; }
}

/// <summary>Puts a still on screen; no duration, so the host's clock decides when it comes down.</summary>
public sealed class ShowImageCommand : ScreenCommandBase
{
    public required string Url { get; init; }

    /// <summary>Sent with the picture: the screen holds no library to look it up in.</summary>
    public ImageScaling Scaling { get; init; }
}

public sealed class HideImageCommand : ScreenCommandBase { }

/// <summary>Every QR code on screen, sent whole on change, like <see cref="SetMarqueeCommand"/>.</summary>
public sealed class SetScreenQrCodesCommand : ScreenCommandBase
{
    public IReadOnlyList<ScreenQrCodePlacement> Codes { get; init; } = [];
}

/// <summary>One code with the venue's defaults resolved; the screen decides nothing itself.</summary>
public sealed class ScreenQrCodePlacement
{
    /// <summary>The finished picture, an SVG data URI; the screen holds no QR library.</summary>
    public required string ImageUrl { get; init; }

    public string? Caption { get; init; }

    public ScreenCorner Corner { get; init; }

    public ScreenQrSize Size { get; init; }

    /// <summary>Modules across; the picture carries no quiet zone (see <see cref="SafeZone"/>).</summary>
    public int Modules { get; init; }

    /// <summary>White margin around the code, in modules, resolved from the venue; never zero.</summary>
    public int SafeZone { get; init; }

    /// <summary>Inset from the screen's edges, as a percentage of the shorter side.</summary>
    public double Offset { get; init; }
}

/// <summary>What's playing between singers; pushed whole on change like the marquee.</summary>
public sealed class SetBreakMusicCardCommand : ScreenCommandBase
{
    /// <summary>False takes the card off; false when the room isn't actually hearing break music.</summary>
    public required bool Enabled { get; init; }

    /// <summary>The track, already composed: a screen holds no library to resolve an id against.</summary>
    public string? Title { get; init; }

    /// <summary>Empty where the provider could not say. An external app need not report one.</summary>
    public string? Artist { get; init; }

    /// <summary>Which corner it sits in; shares the corner rather than covering what's there.</summary>
    public ScreenCorner Corner { get; init; }

    /// <summary>Inset from the edges as a percent of the shorter side; a property of the corner.</summary>
    public double Offset { get; init; }
}

/// <summary>The marquee band. Singers arrive as names; a screen has no library to resolve ids.</summary>
public sealed class SetMarqueeCommand : ScreenCommandBase
{
    /// <summary>False takes the band off the screen entirely; the rest is then ignored.</summary>
    public required bool Enabled { get; init; }

    /// <summary>One line per upcoming turn, in queue order, composed host-side.</summary>
    public IReadOnlyList<string> Singers { get; init; } = [];

    public string? Message { get; init; }

    public MarqueePosition Position { get; init; }

    /// <summary>Null leaves the screen's own default; sent as CSS colours for the screen to render.</summary>
    public string? BackgroundColor { get; init; }

    public string? TextColor { get; init; }

    /// <summary>Text height in pixels; zero leaves the screen's own size.</summary>
    public int FontSizePixels { get; init; }

    /// <summary>Pixels a second; zero leaves the screen's own speed.</summary>
    public int ScrollSpeed { get; init; }

    /// <summary>Holds the "Up next" label at the leading edge instead of scrolling it past.</summary>
    public bool PinLabel { get; init; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(ScreenPlaybackState), "playback")]
[JsonDerivedType(typeof(ScreenBackgroundState), "background")]
public abstract class ScreenStateBase : IScreenState { }

/// <summary>Sent when the background track ends; the song's position clock must not see this.</summary>
public sealed class ScreenBackgroundState : ScreenStateBase
{
    public required string? StreamUrl { get; init; }
    public required bool IsPlaying { get; init; }

    /// <summary>True exactly once per track, when it played out on its own.</summary>
    public required bool HasEnded { get; init; }
}

public sealed class ScreenPlaybackState : ScreenStateBase
{
    /// <summary>The stream the screen is playing, not a file; a screen opens nothing local.</summary>
    public required string? StreamUrl { get; init; }
    public required bool IsPlaying { get; init; }
    public required TimeSpan Position { get; init; }
    public required TimeSpan Duration { get; init; }

    /// <summary>Sample time via the screen's measured offset. Null before one is established.</summary>
    public DateTime? SampledAtUtc { get; init; }
}
