namespace KHost.Abstractions.Models;

/// <summary>
/// What a file's audio track carries. A multi-track karaoke file ships the backing music apart
/// from the voices so a host can decide how much of a guide the singer gets.
/// </summary>
public enum AudioTrackRole
{
    /// <summary>The backing track. Always full level — the others are set against it.</summary>
    Music,

    /// <summary>The original lead vocal, which the singer is there to replace.</summary>
    Lead,

    /// <summary>Harmony and backing voices, which a singer usually wants left in.</summary>
    Backing,
}

/// <summary>
/// One audio stream of a media file. <see cref="Index"/> is the stream's position among the
/// audio streams, not in the container: it is what ffmpeg's <c>0:a:N</c> selects.
/// </summary>
public sealed record AudioTrack(int Index, AudioTrackRole Role, string Name)
{
    /// <summary>
    /// Reads a role out of a track's name. Order matters: a track called "Backing Vocal" is
    /// voices, while "Backing Track" is the music, and both contain the same word.
    /// </summary>
    public static AudioTrackRole? RoleFor(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        var text = name.Trim().ToLowerInvariant();

        if (text.Contains("instrument") || text.Contains("karaoke") || text.Contains("music")
            || text.Contains("backing track"))
            return AudioTrackRole.Music;

        if (text.Contains("lead")) return AudioTrackRole.Lead;

        if (text.Contains("back") || text.Contains("harmon") || text.Contains("choir"))
            return AudioTrackRole.Backing;

        // A track named only "Vocal" is the one the singer is replacing; a harmony track says so.
        if (text.Contains("vocal")) return AudioTrackRole.Lead;

        return null;
    }
}
