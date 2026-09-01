using System.Globalization;
using KHost.Common.Monetary;

namespace KHost.UnitTests.Common.Monetary;

public class MoneyExtensionsTests
{
    public MoneyExtensionsTests()
        // Currency formatting is culture-dependent; pin one so the expectations mean something.
        => CultureInfo.CurrentCulture = new CultureInfo("en-US");

    [Theory]
    [InlineData(0, "$0.00")]
    [InlineData(1, "$0.01")]
    [InlineData(50, "$0.50")]
    [InlineData(1250, "$12.50")]
    [InlineData(100000, "$1,000.00")]
    [InlineData(9999999, "$99,999.99")]
    public void ToCurrency_FormatsCentsAsMoney(int cents, string expected)
        => Assert.Equal(expected, cents.CentsToCurrencyString());

    [Theory]
    [InlineData(29, 0.29)]
    [InlineData(1250, 12.50)]
    [InlineData(1, 0.01)]
    public void ToDecimal_ConvertsWithoutLosingCents(int cents, double expected)
        => Assert.Equal((decimal)expected, cents.CentsToDecimal());

    [Fact]
    public void Cents_AddUpExactly_WhereFloatingPointWouldNot()
    {
        // 0.1 + 0.2 != 0.3 in binary floating point; in cents it is just 10 + 20.
        int[] tips = [10, 20];

        Assert.Equal(30, tips.Sum());
        Assert.Equal("$0.30", tips.Sum().CentsToCurrencyString());
    }
}
