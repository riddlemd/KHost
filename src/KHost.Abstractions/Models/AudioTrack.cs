namespace KHost.Abstractions.Models;

/// <summary>What a track carries; multi-track karaoke ships backing music apart from the voices.</summary>
public enum AudioTrackRole
{
    /// <summary>The backing track. Always full level; the others are set against it.</summary>
    Music,

    /// <summary>The original lead vocal, which the singer is there to replace.</summary>
    Lead,

    /// <summary>Harmony and backing voices, which a singer usually wants left in.</summary>
    Backing,
}

/// <summary>One audio stream in a file with more than one to choose between or mix.</summary>
/// <param name="Index">The stream's position among the file's audio tracks, in file order, zero-based.</param>
/// <param name="Role">What the stream carries.</param>
/// <param name="Name">A short label for the stream, for a host choosing between tracks by hand.</param>
public sealed record AudioTrack(int Index, AudioTrackRole Role, string Name)
{
    /// <summary>The singer a <see cref="AudioTrackRole.Lead"/> belongs to, as the file names them,
    /// so a host can ride each singer's lead on its own.</summary>
    /// <remarks>Null for a lead no singer is named on, and for every track that is not a lead.
    /// Two leads with the same voice are one singer's part and share one level.</remarks>
    public string? Voice { get; init; }
}
