namespace KHost.Domain.Monetary;

/// <summary>
/// Money is stored as a whole number of cents. SQLite has no decimal type, so a decimal is kept as
/// text — exact, but lexicographic: "100.00" sorts before "9.00" and SUM() does not work. Cents are
/// an INTEGER, which sorts and sums correctly, and never introduces the rounding error a REAL would.
/// </summary>
public static class MoneyExtensions
{
    /// <summary>Formats cents for display, e.g. 1250 becomes "$12.50" under a US culture.</summary>
    public static string ToCurrency(this int cents) => (cents / 100m).ToString("C");

    /// <summary>The dollars-and-cents value, for the rare caller that needs to do decimal maths.</summary>
    public static decimal ToDecimal(this int cents) => cents / 100m;
}
