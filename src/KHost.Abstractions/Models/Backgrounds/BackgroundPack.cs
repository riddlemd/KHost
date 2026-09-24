namespace KHost.Abstractions.Models.Backgrounds;

/// <summary>What a pack folder turned out to hold.</summary>
/// <remarks>A pack is a flat folder of clips, each optionally with a still of the same name beside
/// it. There is nothing to author and nothing to keep in step — adding a background is copying a
/// file in.</remarks>
public sealed class BackgroundPack
{
    /// <summary>The backgrounds found. Empty when the pack has none or could not be read.</summary>
    public IReadOnlyList<BackgroundPackEntry> Entries { get; init; } = [];

    /// <summary>Why <see cref="Entries"/> is empty; <see cref="BackgroundPackProblem.None"/> otherwise.</summary>
    public BackgroundPackProblem Problem { get; init; }

    /// <summary>Whether there is at least one background to choose from.</summary>
    public bool HasAny => Entries.Count > 0;
}
