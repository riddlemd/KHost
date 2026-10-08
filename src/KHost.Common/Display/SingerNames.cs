using KHost.Abstractions.Models;

namespace KHost.Common.Display;

/// <summary>Which name a display shows for a singer taking a turn.</summary>
public static class SingerNames
{
    /// <summary>The name recorded on the turn, unless the venue does not allow aliases or none was
    /// recorded; then the singer's own name.</summary>
    /// <remarks>Read off the turn, not the account: a song-first remote lets a guest type a name per
    /// pick, so every turn can carry its own.</remarks>
    /// <param name="turn">The singer's turn; null when nothing is queued for them.</param>
    /// <param name="singer">The singer the turn belongs to.</param>
    /// <param name="aliasesAllowed">Whether the venue lets a recorded name stand in for the singer's.</param>
    public static string DisplayedFor(Performance? turn, KHostUser singer, bool aliasesAllowed)
    {
        var recorded = turn?.SungAs?.Trim();

        return string.IsNullOrEmpty(recorded) || !aliasesAllowed ? singer.Name : recorded;
    }
}
