namespace KHost.Abstractions.Models;

/// <summary>
/// What is playing between singers, as far as the console needs to say it. A provider driving
/// another app fills in what that app reports and nothing more.
/// </summary>
public sealed class BreakMusicTrack
{
    public required string Title { get; init; }

    public string Artist { get; init; } = string.Empty;

    /// <summary>Null when the provider cannot say — an external app need not report one.</summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>Set only by a provider playing out of the host's own library.</summary>
    public Guid? MediaId { get; init; }
}
