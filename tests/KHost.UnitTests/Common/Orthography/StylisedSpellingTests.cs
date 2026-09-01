using KHost.Common.Orthography;

namespace KHost.UnitTests.Common.Orthography;

public class StylisedSpellingTests
{
    [Theory]
    [InlineData("Ke$ha", "Kesha")]
    [InlineData("P!nk", "Pink")]
    [InlineData("h@ppy", "happy")]
    // Substitutes a lowercase letter; the fold that follows lowercases everything anyway.
    [InlineData("A$AP Rocky", "AsAP Rocky")]
    public void Resolve_ReplacesSymbolsStandingInForLetters(string value, string expected)
        => Assert.Equal(expected, StylisedSpelling.Resolve(value));

    [Theory]
    [InlineData("Blink-182")]
    [InlineData("50 Cent")]
    [InlineData("Deadmau5")]
    public void Resolve_LeavesDigitsAlone(string value)
        // The rule that turns Deadmau5 into deadmaus also ruins these, so digits stay put.
        => Assert.Equal(value, StylisedSpelling.Resolve(value));

    [Theory]
    [InlineData("Björk")]
    [InlineData("Ordinary Artist")]
    [InlineData("O'Connor")]
    public void Resolve_LeavesOrdinaryTextAlone(string value)
        => Assert.Equal(value, StylisedSpelling.Resolve(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Resolve_ReturnsEmpty_ForNothing(string? value)
        => Assert.Equal(string.Empty, StylisedSpelling.Resolve(value));
}
