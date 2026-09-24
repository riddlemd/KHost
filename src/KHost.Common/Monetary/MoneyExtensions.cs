namespace KHost.Common.Monetary;

/// <summary>Formatting for amounts KHost stores as integer cents.</summary>
public static class MoneyExtensions
{
    /// <summary>Formats cents for display, e.g. 1250 becomes "$12.50" under a US culture.</summary>
    public static string CentsToCurrencyString(this int cents) => (cents / 100m).ToString("C");
}
