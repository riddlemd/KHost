using KHost.Abstractions.Models;

namespace KHost.Common.Media;

/// <summary>Guesses an <see cref="AudioTrackRole"/> from a probed track's name, for a provider that ships no roles of its own.</summary>
public static class AudioTrackRoles
{
    /// <summary>Reads a role out of a track's name; order matters here.</summary>
    /// <remarks>"Backing Vocal" is voices but "Backing Track" is music, and both contain "backing".</remarks>
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
