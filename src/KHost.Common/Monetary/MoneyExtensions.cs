namespace KHost.Common.Monetary;

public static class MoneyExtensions
{
    /// <summary>Formats cents for display, e.g. 1250 becomes "$12.50" under a US culture.</summary>
    public static string CentsToCurrencyString(this int cents) => (cents / 100m).ToString("C");
}
