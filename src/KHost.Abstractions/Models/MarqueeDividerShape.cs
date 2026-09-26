namespace KHost.Abstractions.Models;

/// <summary>The glyph a screen draws between marquee entries. Dot is the zero value, so an old row
/// defaults to today's look with no backfill.</summary>
public enum MarqueeDividerShape
{
    /// <summary>The screen's own separator, "•" — today's look.</summary>
    Dot,

    /// <summary>"◆"</summary>
    Diamond,

    /// <summary>"★"</summary>
    Star,

    /// <summary>"/"</summary>
    Slash,

    /// <summary>"|"</summary>
    Pipe,

    /// <summary>"♪"</summary>
    Note,

    /// <summary>No divider is drawn between entries at all.</summary>
    None,
}
