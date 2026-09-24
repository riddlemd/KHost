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
/// <remarks>The same file renders differently for different targets — a kit needs an encode for a
/// receiver and none for a screen that mixes — so this is part of the question, not context a
/// renderer is expected to look up.</remarks>
public sealed class RenderTarget
{
    /// <summary>Whether the one connected display takes the stems unmixed and rides the levels itself.</summary>
    public bool MixesStems { get; init; }

    /// <summary>Nothing connected, so nothing is worth producing beyond what a later target needs.</summary>
    public static RenderTarget None { get; } = new();
}
