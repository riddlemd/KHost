namespace KHost.UserInterface.Models;

/// <summary>Which shape the song controls take. Presentation only — both drive the same values.</summary>
public enum SongControlStyle
{
    Sliders,
    Dials,
}

/// <summary>
/// Where a control's coloured span starts and ends, as fractions of its travel. Shared by the
/// slider and the dial so the same value can never read one way on one and another on the other.
/// </summary>
public readonly record struct SongControlSpan(double From, double To, bool IsPositive, bool IsAtRest)
{
    /// <summary>
    /// Zero is where all of them rest, and the same arithmetic serves both kinds: it is the middle
    /// of a key or tempo travel, and the left edge of a volume that cannot go negative.
    /// </summary>
    public static SongControlSpan For(int value, int min, int max)
    {
        var travel = max - min == 0 ? 1 : max - min;

        double Fraction(double at) => (at - min) / travel;

        var rest = Fraction(0);

        return value.CompareTo(0) switch
        {
            0 => new SongControlSpan(rest, rest, false, true),
            > 0 => new SongControlSpan(rest, Fraction(value), true, false),
            _ => new SongControlSpan(Fraction(value), rest, false, false),
        };
    }

    /// <summary>
    /// Nothing at rest. A round line cap paints a dot even on a zero-length dash, so the dial has
    /// to be told to draw in nothing rather than to draw nothing.
    /// </summary>
    public string Colour => IsAtRest
        ? "transparent"
        : IsPositive ? "var(--kh-success)" : "var(--kh-danger)";

    public string ValueClass => IsAtRest
        ? ""
        : IsPositive ? "kh-song-control__value--up" : "kh-song-control__value--down";
}
