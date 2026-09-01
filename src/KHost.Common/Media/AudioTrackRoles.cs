using KHost.Abstractions.Models;

namespace KHost.Common.Media;

public static class AudioTrackRoles
{
    /// <summary>
    /// Reads a role out of a track's name. Order matters: a track called "Backing Vocal" is
    /// voices, while "Backing Track" is the music, and both contain the same word.
    /// </summary>
    public static AudioTrackRole? FromTrackName(string? name)
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
