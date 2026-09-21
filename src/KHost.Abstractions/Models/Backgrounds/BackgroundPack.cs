namespace KHost.Abstractions.Models.Backgrounds;

/// <summary>What a pack folder turned out to hold.</summary>
/// <remarks>A pack is a flat folder of clips, each optionally with a still of the same name beside
/// it. There is nothing to author and nothing to keep in step — adding a background is copying a
/// file in.</remarks>
public sealed class BackgroundPack
{
    public IReadOnlyList<BackgroundPackEntry> Entries { get; init; } = [];

    public BackgroundPackProblem Problem { get; init; }

    public bool HasAny => Entries.Count > 0;
}
