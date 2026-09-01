namespace KHost.Common.Monetary;

public static class MoneyExtensions
{
    /// <summary>Formats cents for display, e.g. 1250 becomes "$12.50" under a US culture.</summary>
    public static string ToCurrency(this int cents) => (cents / 100m).ToString("C");

    /// <summary>The dollars-and-cents value, for the rare caller that needs to do decimal maths.</summary>
    public static decimal ToDecimal(this int cents) => cents / 100m;
}
