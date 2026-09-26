namespace KHost.Abstractions.Models;

/// <summary>Everything a renderer needs to decide what to hand back for one play.</summary>
public sealed class MediaRenderRequest
{
    /// <summary>Full path to the source file on disk.</summary>
    public required string FilePath { get; init; }

    /// <summary>Song position to begin at. A whole-file rendition ignores it and is seeked instead.</summary>
    public TimeSpan StartOffset { get; init; }

    /// <summary>Semitones from the written key; zero for the original.</summary>
    public int Pitch { get; init; }

    /// <summary>Percent either side of recorded speed; zero for the original.</summary>
    public int Tempo { get; init; }

    /// <summary>The levels asked for, when the song has separate voices to balance.</summary>
    public AudioMix? Mix { get; init; }

    /// <summary>What the thing being played on can do, which decides what is worth producing.</summary>
    public required RenderTarget Target { get; init; }
}

/// <summary>What the display this is being rendered for can do with what it is handed.</summary>
/// <remarks>The same file renders differently for different targets — stems need an encode for a
/// receiver and none for a screen that mixes — so this is part of the question, not context a
/// renderer is expected to look up. A renderer answering with stems may ignore it: the host makes
/// that encode from them.</remarks>
public sealed class RenderTarget
{
    /// <summary>Whether the one connected display takes the stems unmixed and rides the levels itself.</summary>
    public bool MixesStems { get; init; }

    /// <summary>Whether the display cannot draw a song's timed words itself and wants them in the
    /// picture.</summary>
    /// <remarks>A request, not a demand. The host's own encode honours it for any song some
    /// <see cref="Services.ITimedLyricsProvider"/> supplies timed words for, painting them into the
    /// picture, so a renderer that declines, or answers with stems alone, hands such a song to an
    /// encode that burns them in. A renderer that answers with a stream of its own MAY honour it; one
    /// that cannot returns the rendition it would have returned anyway rather than declining or
    /// failing, and that song reaches the display without its words.</remarks>
    public bool BurnLyrics { get; init; }

    /// <summary>Nothing connected, or nothing special asked for: every flag false.</summary>
    public static RenderTarget None { get; } = new();
}
