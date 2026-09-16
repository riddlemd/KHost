using KHost.Abstractions.Models;

namespace KHost.Common.Performances;

public static class PerformanceNames
{
    /// <summary>Shown where a performance has no name and nothing left to look one up from.</summary>
    public const string UnknownSinger = "Unknown";

    /// <summary>
    /// What name this performance goes by on screen. One rule in one place, because the answer has
    /// three inputs and every surface that shows a singer needs the same one — seven copies of it
    /// would be seven chances to forget the venue's say in it.
    ///
    /// <paramref name="aliasesAllowed"/> is the venue's: with it off, a recorded name that differs
    /// from the singer's own is ignored and the room sees the singer it knows. The name is still
    /// recorded, so turning the setting on shows what was already there.
    ///
    /// Falls back to <paramref name="singer"/> when nothing was recorded, and to
    /// <see cref="UnknownSinger"/> when the singer is gone too — a sung performance outlives the
    /// row that named it, which is the whole reason the name is kept here.
    /// </summary>
    public static string DisplayName(this Performance performance, KHostUser? singer, bool aliasesAllowed)
    {
        var recorded = performance.SungAs?.Trim();
        var known = singer?.Name?.Trim();

        if (string.IsNullOrEmpty(recorded))
            return string.IsNullOrEmpty(known) ? UnknownSinger : known;

        if (!aliasesAllowed && !string.IsNullOrEmpty(known))
            return known;

        return recorded;
    }
}
