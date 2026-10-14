namespace KHost.Abstractions.Models;

/// <summary>The glyph a screen draws between marquee entries. Dot is the zero value, so an old row
/// defaults to today's look with no backfill.</summary>
public enum MarqueeDividerShape
{
    /// <summary>The screen's own separator, "•" — today's look.</summary>
    Dot = 0,

    /// <summary>"◆"</summary>
    Diamond = 1,

    /// <summary>"★"</summary>
    Star = 2,

    /// <summary>"/"</summary>
    Slash = 3,

    /// <summary>"|"</summary>
    Pipe = 4,

    /// <summary>"♪"</summary>
    Note = 5,

    /// <summary>No divider is drawn between entries at all.</summary>
    None = 6,
}
