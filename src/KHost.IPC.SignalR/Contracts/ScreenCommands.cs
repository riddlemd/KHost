using KHost.Abstractions.Models;
using System.Text.Json.Serialization;

namespace KHost.IPC.SignalR.Contracts;

/// <summary>Base for every command the host sends to its own LocalScreen app.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(LoadMediaCommand), "loadMedia")]
[JsonDerivedType(typeof(PlayCommand), "play")]
[JsonDerivedType(typeof(PauseCommand), "pause")]
[JsonDerivedType(typeof(StopCommand), "stop")]
[JsonDerivedType(typeof(SeekCommand), "seek")]
[JsonDerivedType(typeof(SetVolumeCommand), "setVolume")]
[JsonDerivedType(typeof(SetStemVolumeCommand), "setStemVolume")]
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
[JsonDerivedType(typeof(ShowNextSingerCommand), "showNextSinger")]
[JsonDerivedType(typeof(SetTimedLyricsCommand), "setTimedLyrics")]
public abstract class ScreenCommandBase : IScreenCommand { }

/// <summary>Loads one song or clip, ready to play but not playing: a stream to play end to end,
/// stems to mix, or both.</summary>
public sealed class LoadMediaCommand : ScreenCommandBase
{
    /// <summary>The host encodes; the display plays the stream, with no decoder of its own.</summary>
    /// <remarks>Null when nothing was encoded because the display plays the parts itself. Never
    /// null at the same time as <see cref="Stems"/> is empty — that would be a song with nowhere
    /// to come from.</remarks>
    public string? StreamUrl { get; init; }

    /// <summary>Song position the stream's zero maps to; add it before reporting a position.</summary>
    public TimeSpan StreamStartOffset { get; init; }

    /// <summary>Tempo percent the stream was encoded at; scales every position crossing it.</summary>
    public int Tempo { get; init; }

    /// <summary>Stems for a display that mixes them itself; empty when the host already mixed.</summary>
    /// <remarks><see cref="StreamUrl"/> may be set beside these, for a display that turns out not to
    /// mix; when it is null, the stems are the only way the song comes out.</remarks>
    public IReadOnlyList<StemSource> Stems { get; init; } = [];
}

/// <summary>Moves one voice against the music on a display doing its own mixing.</summary>
/// <remarks>By role rather than by index, because that is how a host asks for it — the lead and the
/// backing are what the console offers. A display with no such stem ignores it.
///
/// <para>This is the whole reason a display mixes: the host's own mix is baked into the stream, so
/// changing it means reopening the stream mid-song. This moves a gain instead.</para></remarks>
public sealed class SetStemVolumeCommand : ScreenCommandBase
{
    /// <summary>Which voice this changes the level of.</summary>
    public required AudioTrackRole Role { get; init; }

    /// <summary>The singer whose lead this moves, matched exactly against a stem's own voice; null
    /// moves the stems of <see cref="Role"/> that carry none.</summary>
    public string? Voice { get; init; }

    /// <summary>Gain against the music, 0-100; the music track itself has no level of its own.</summary>
    public required int Volume { get; init; }
}

/// <summary>Resumes playback from the current position.</summary>
public sealed class PlayCommand : ScreenCommandBase { }

/// <summary>Pauses playback at the current position.</summary>
public sealed class PauseCommand : ScreenCommandBase { }

/// <summary>Stops playback and releases the current song.</summary>
public sealed class StopCommand : ScreenCommandBase
{
    /// <summary>How long to fade before stopping; null or zero stops at once.</summary>
    public TimeSpan? FadeDuration { get; init; }
}

/// <summary>Moves playback to a new position in the current song.</summary>
public sealed class SeekCommand : ScreenCommandBase
{
    /// <summary>The song position to seek to.</summary>
    public required TimeSpan Position { get; init; }
}

/// <summary>Sets the master volume of the current song.</summary>
public sealed class SetVolumeCommand : ScreenCommandBase
{
    /// <summary>Linear gain, 0.0-1.0.</summary>
    public required float Volume { get; init; }
}

/// <summary>Blanks the picture without stopping playback, so the song carries on underneath.</summary>
public sealed class SetVideoCommand : ScreenCommandBase
{
    /// <summary>False blanks the picture; true restores it. The song keeps playing either way.</summary>
    public required bool Enabled { get; init; }
}

/// <summary>Second audio channel for break music and an ad's bed; it has no song position.</summary>
public sealed class LoadBackgroundCommand : ScreenCommandBase
{
    /// <summary>Where the display fetches the background audio.</summary>
    public required string StreamUrl { get; init; }

    /// <summary>Starts as soon as it can play, sparing the caller a second round trip.</summary>
    public bool AutoPlay { get; init; } = true;
}

/// <summary>Resumes the background channel.</summary>
public sealed class PlayBackgroundCommand : ScreenCommandBase { }

/// <summary>Pauses the background channel.</summary>
public sealed class PauseBackgroundCommand : ScreenCommandBase { }

/// <summary>Stops the background channel.</summary>
public sealed class StopBackgroundCommand : ScreenCommandBase
{
    /// <summary>How long to fade before stopping; null or zero stops at once.</summary>
    public TimeSpan? FadeDuration { get; init; }
}

/// <summary>Separate from <see cref="SetVolumeCommand"/>: a bed sits under the song's fader.</summary>
public sealed class SetBackgroundVolumeCommand : ScreenCommandBase
{
    /// <summary>Linear gain, 0.0-1.0.</summary>
    public required float Volume { get; init; }
}

/// <summary>Puts a still on screen; no duration, so the host's clock decides when it comes down.</summary>
public sealed class ShowImageCommand : ScreenCommandBase
{
    /// <summary>Where the display fetches the still image.</summary>
    public required string Url { get; init; }

    /// <summary>How the image fills the screen.</summary>
    /// <remarks>Sent with the picture: the screen holds no library to look it up in.</remarks>
    public ImageScaling Scaling { get; init; }
}

/// <summary>Takes the still down.</summary>
public sealed class HideImageCommand : ScreenCommandBase { }

/// <summary>Names who is up, put on the screens by a host between songs.</summary>
/// <remarks>Finished strings, not ids: the screen holds no queue to look a singer up in. It stands
/// until the next thing is drawn, so nothing here takes it down and no timer is needed.</remarks>
public sealed class ShowNextSingerCommand : ScreenCommandBase
{
    /// <summary>The name the room should hear, which is the alias where the venue allows one.</summary>
    public required string Singer { get; init; }

    /// <summary>Null for a singer on the list with nothing queued yet: the card names them alone
    /// rather than promising a song that does not exist.</summary>
    public string? Song { get; init; }

    /// <summary>Null where the queued song has no artist recorded.</summary>
    public string? Artist { get; init; }
}

/// <summary>Every QR code on screen, sent whole on change, like <see cref="SetMarqueeCommand"/>.</summary>
public sealed class SetScreenQrCodesCommand : ScreenCommandBase
{
    /// <summary>Every code currently shown; empty takes every code off the screen.</summary>
    public IReadOnlyList<ScreenQrCodePlacement> Codes { get; init; } = [];
}

/// <summary>One code with the venue's defaults resolved; the screen decides nothing itself.</summary>
public sealed class ScreenQrCodePlacement
{
    /// <summary>The finished picture, an SVG data URI; the screen holds no QR library.</summary>
    public required string ImageUrl { get; init; }

    /// <summary>Text shown beside the code. Null shows the code alone.</summary>
    public string? Caption { get; init; }

    /// <summary>Which corner it sits in.</summary>
    public OverlayCorner Corner { get; init; }

    /// <summary>How big it is drawn.</summary>
    public QrCodeSize Size { get; init; }

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
    public OverlayCorner Corner { get; init; }

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

    /// <summary>The venue's own line, e.g. a drink special. Null shows only singers.</summary>
    public string? Message { get; init; }

    /// <summary>Which edge of the screen the band sits against.</summary>
    public MarqueePosition Position { get; init; }

    /// <summary>Null leaves the screen's own default; sent as CSS colours for the screen to draw with.</summary>
    public string? BackgroundColor { get; init; }

    /// <summary>Null leaves the screen's own default; sent as CSS colours for the screen to draw with.</summary>
    public string? TextColor { get; init; }

    /// <summary>Text height in pixels; zero leaves the screen's own size.</summary>
    public int FontSizePixels { get; init; }

    /// <summary>Pixels a second; zero leaves the screen's own speed.</summary>
    public int ScrollSpeed { get; init; }

    /// <summary>Holds the "Up next" label at the leading edge instead of scrolling it past.</summary>
    public bool PinLabel { get; init; }
}

/// <summary>The words to draw over this song, or null to draw none.</summary>
/// <remarks>Sent with the load rather than with the transport: it is the whole timing document, so
/// it must not ride a message sent twice a second. The screen holds it until the next load.
///
/// Drawing it is the screen's job entirely — the host sends the words and never learns whether
/// anything was drawn.</remarks>
public sealed class SetTimedLyricsCommand : ScreenCommandBase
{
    /// <summary>The timing, or null when this song has none and the screen should clear what it holds.</summary>
    public required TimedLyrics? Lyrics { get; init; }

    /// <summary>The card naming the song and its singer until the first page of words arrives, or
    /// null for none.</summary>
    /// <remarks>Carried here rather than on its own command because it exists only beside words the
    /// screen draws itself: a picture that carries its own words carries its own intro too. Sent
    /// together, the screen can never hold one song's card over another song's words.</remarks>
    public ScreenIntroCard? Intro { get; init; }
}

/// <summary>What the intro card says, as finished strings: the screen holds no library or queue.</summary>
public sealed class ScreenIntroCard
{
    /// <summary>The song's title.</summary>
    public required string Title { get; init; }

    /// <summary>Null where the song has no artist recorded.</summary>
    public string? Artist { get; init; }

    /// <summary>The name the room should hear, the alias where the venue allows one; null when
    /// there is nobody to name.</summary>
    public string? Singer { get; init; }
}

/// <summary>Base for state the LocalScreen app reports back to the host.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(ScreenPlaybackState), "playback")]
[JsonDerivedType(typeof(ScreenBackgroundState), "background")]
public abstract class ScreenStateBase : IScreenState { }

/// <summary>Sent when the background track ends; the song's position clock must not see this.</summary>
public sealed class ScreenBackgroundState : ScreenStateBase
{
    /// <summary>The background stream currently loaded, or null when none is.</summary>
    public required string? StreamUrl { get; init; }

    /// <summary>Whether the background channel is currently playing.</summary>
    public required bool IsPlaying { get; init; }

    /// <summary>True exactly once per track, when it played out on its own.</summary>
    public required bool HasEnded { get; init; }
}

/// <summary>Where the current song is, reported back for the host's own position clock.</summary>
public sealed class ScreenPlaybackState : ScreenStateBase
{
    /// <summary>The stream the screen is playing, not a file; a screen opens nothing local.</summary>
    public required string? StreamUrl { get; init; }

    /// <summary>Whether the song is currently playing.</summary>
    public required bool IsPlaying { get; init; }

    /// <summary>How far into the song playback currently is.</summary>
    public required TimeSpan Position { get; init; }

    /// <summary>The song's total length.</summary>
    public required TimeSpan Duration { get; init; }

    /// <summary>Sample time via the screen's measured offset. Null before one is established.</summary>
    public DateTime? SampledAtUtc { get; init; }
}
