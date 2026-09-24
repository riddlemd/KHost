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
}

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
