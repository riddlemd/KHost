namespace KHost.Abstractions.Models;

/// <summary>The words of a song with the moment each one is sung, for a screen to draw itself.</summary>
/// <remarks>Not <see cref="Lyrics"/>, which is a block of text a host reads in a dialog. This is the
/// chase: what is lit, when, and where on the screen.
///
/// It exists so a provider that ships its own container can hand over the timing without the host
/// learning the format. The host routes it and the screen draws it; neither parses anything.</remarks>
public sealed record TimedLyrics
{
    /// <summary>How long the song runs, as the timing itself states it.</summary>
    /// <remarks>Taken from the timing rather than from the audio: the two disagree by a little on
    /// most files, and the chase has to follow the one the syllables were written against.</remarks>
    public required double DurationSeconds { get; init; }

    /// <summary>The screenfuls, in the order they are shown.</summary>
    public IReadOnlyList<LyricPage> Pages { get; init; } = [];

    /// <summary>The space <see cref="LyricBox"/> coordinates are measured in.</summary>
    /// <remarks>A provider's layout has its own units and no pixels — the screen scales this box to
    /// its own size. Sent rather than assumed: a provider that lays out against a different shape
    /// would otherwise have every line drawn in the wrong place, and nothing would say why.</remarks>
    public required LyricBox Bounds { get; init; }

    /// <summary>Right-to-left songs exist and a page says so for itself.</summary>
    public bool IsRightToLeft { get; init; }

    /// <summary>The stretches with nothing to sing that the timing marks with a count-in, in song
    /// order.</summary>
    /// <remarks>Empty when the timing marks none, which most do. Nothing is inferred from a gap
    /// between syllables: a screen draws a count-in only where one was given.</remarks>
    public IReadOnlyList<LyricCountIn> CountIns { get; init; } = [];
}

/// <summary>A bar that fills across a stretch with nothing to sing, counting the singer back in.</summary>
/// <remarks>Drawn on its own, not as part of a page: it belongs to the gap, and a gap usually has no
/// page on screen at all.</remarks>
public sealed record LyricCountIn
{
    /// <summary>When the bar appears and starts to fill, as an absolute song position.</summary>
    public required double StartSeconds { get; init; }

    /// <summary>When the bar is full and gone, as an absolute song position.</summary>
    /// <remarks>Usually where the next words start, but that is the timing's choice, not a promise
    /// that it meets a syllable.</remarks>
    public required double EndSeconds { get; init; }

    /// <summary>Where the bar sits, in the space <see cref="TimedLyrics.Bounds"/> describes.</summary>
    public required LyricBox Position { get; init; }

    /// <summary>How long one beat of the countdown lasts, or zero for no countdown.</summary>
    public double StepSeconds { get; init; }

    /// <summary>How many beats are counted down before <see cref="EndSeconds"/>, the last one being
    /// one. Zero for no countdown.</summary>
    public int Steps { get; init; }

    /// <summary>The colour of the part already filled, or null to leave it to the screen's theme.</summary>
    public LyricColor? Active { get; init; }

    /// <summary>The colour of the part still to fill, or null for the screen's theme.</summary>
    public LyricColor? Inactive { get; init; }

    /// <summary>The outline's colour, or null for the screen's theme.</summary>
    public LyricColor? Border { get; init; }

    /// <summary>The outline's thickness, in the timing's own units; zero for none.</summary>
    public double BorderWidth { get; init; }
}

/// <summary>One screenful of words, and when it arrives and leaves.</summary>
public sealed record LyricPage
{
    /// <summary>When the page appears, and when it is gone.</summary>
    /// <remarks>Both are absolute song positions, already resolved from whatever fades the source
    /// described. A provider that states no window works one out from its own syllables — the
    /// screen must never have to guess when to draw a page.</remarks>
    public required double ShowFromSeconds { get; init; }

    /// <inheritdoc cref="ShowFromSeconds"/>
    public required double ShowUntilSeconds { get; init; }

    /// <summary>The lines on it, top to bottom.</summary>
    public IReadOnlyList<LyricLine> Lines { get; init; } = [];

    /// <summary>The colour of a syllable already sung, or null to leave it to the screen's theme.</summary>
    public LyricColor? Active { get; init; }

    /// <summary>The colour of a syllable not yet reached, or null for the screen's theme.</summary>
    public LyricColor? Inactive { get; init; }

    /// <summary>The singer who sings this page, named as the media names them.</summary>
    /// <remarks>Taken as the media spells it, never corrected, so it equals an
    /// <see cref="AudioTrack.Voice"/> only when the media names the singer the same way on their lead
    /// stem. Null when the media does not say who sings.</remarks>
    public string? Voice { get; init; }
}

/// <summary>One line of words, and where on the page it sits.</summary>
public sealed record LyricLine
{
    /// <summary>Where the line goes, or null to let the screen stack it with its neighbours.</summary>
    public LyricBox? Position { get; init; }

    /// <summary>Its syllables, in sung order.</summary>
    /// <remarks>Syllables, not words: the chase reveals a word part by part, and a provider that
    /// only knows whole words says so by giving each one a single syllable.</remarks>
    public IReadOnlyList<LyricSyllable> Syllables { get; init; } = [];

    /// <summary>A marker that runs into the line ahead of its first syllable, or null for none.</summary>
    public LyricLeadIn? LeadIn { get; init; }
}

/// <summary>A marker that travels up to a line's first word, so a singer coming out of a silence
/// sees the moment to start.</summary>
/// <remarks>It arrives where and when the line starts — at the line's leading edge, as its first
/// syllable is lit — so only where it sets off from is carried. A line with no syllables or no
/// <see cref="LyricLine.Position"/> gives it nowhere to arrive, and nothing is drawn.</remarks>
/// <param name="StartSeconds">Song position the marker appears and sets off.</param>
/// <param name="X">Where it sets off, in the timing's own units, on the same axis as the line's
/// <see cref="LyricBox.X"/>. The distance from here to the line's left edge is the run it makes;
/// a right-to-left song makes the same run into the line's right edge from outside it.</param>
public sealed record LyricLeadIn(double StartSeconds, double X);

/// <summary>One piece of a word, lit between two moments.</summary>
/// <remarks><see cref="Text"/> carries its own spacing. Joining syllables with a space inserts one
/// inside every word that was split.</remarks>
/// <param name="StartSeconds">Song position this syllable is lit from.</param>
/// <param name="EndSeconds">Song position this syllable is lit until.</param>
/// <param name="Text">The syllable's own text, including any spacing it carries.</param>
public sealed record LyricSyllable(double StartSeconds, double EndSeconds, string Text);

/// <summary>A rectangle in the timing's own coordinate space, not in pixels.</summary>
/// <param name="X">Left edge, in the timing's own units.</param>
/// <param name="Y">Top edge, in the timing's own units.</param>
/// <param name="Width">Width, in the timing's own units.</param>
/// <param name="Height">Height, in the timing's own units.</param>
public sealed record LyricBox(double X, double Y, double Width, double Height);

/// <summary>A colour the timing asked for.</summary>
/// <param name="R">Red, 0-255.</param>
/// <param name="G">Green, 0-255.</param>
/// <param name="B">Blue, 0-255.</param>
public sealed record LyricColor(byte R, byte G, byte B);
