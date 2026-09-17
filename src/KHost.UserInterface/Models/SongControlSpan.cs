namespace KHost.UserInterface.Models;

/// <summary>Which shape the song controls take. Presentation only: both drive the same values.</summary>
public enum SongControlStyle
{
    Sliders,
    Dials,
}

/// <summary>Where a control's coloured span starts and ends, as fractions of its travel.</summary>
/// <remarks>Shared by the slider and dial so the same value never reads two ways.</remarks>
public readonly record struct SongControlSpan(double From, double To, bool IsPositive, bool IsAtRest)
{
    /// <summary>Zero is where all of them rest, so the same arithmetic works for every control.</summary>
    /// <remarks>Middle of key/tempo travel; left edge of a volume that cannot go negative.</remarks>
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

    /// <summary>Nothing at rest paints as transparent, not a zero-length coloured dash.</summary>
    /// <remarks>A round line cap draws a dot even on a zero-length dash.</remarks>
    public string Colour => IsAtRest
        ? "transparent"
        : IsPositive ? "var(--kh-success)" : "var(--kh-danger)";

    public string ValueClass => IsAtRest
        ? ""
        : IsPositive ? "kh-song-control__value--up" : "kh-song-control__value--down";
}
