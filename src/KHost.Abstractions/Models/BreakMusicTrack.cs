namespace KHost.Abstractions.Models;

/// <summary>What is playing between singers; a provider fills in only what its app reports.</summary>
public sealed class BreakMusicTrack
{
    public required string Title { get; init; }

    public string Artist { get; init; } = string.Empty;

    /// <summary>Null when the provider cannot say; an external app need not report one.</summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>Set only by a provider playing out of the host's own library.</summary>
    public Guid? MediaId { get; init; }
}
