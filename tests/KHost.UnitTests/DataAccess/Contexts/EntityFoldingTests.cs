using KHost.DataAccess.Contexts;

namespace KHost.UnitTests.DataAccess.Contexts;

/// <summary>The rule every folded column is written with, and every search is compared against.</summary>
public class EntityFoldingTests
{
    [Theory]
    [InlineData("Beyoncé", "beyonce")]
    [InlineData("  Björk  ", "bjork")]
    [InlineData("AC/DC", "ac/dc")]
    public void Fold_StripsAccentsAndCase(string input, string expected)
        => Assert.Equal(expected, EntityFolding.Fold(input));

    /// <summary>Never null, so a folded value is always safe to compare against a stored one.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Fold_NothingIn_IsEmptyNotNull(string? input)
        => Assert.Equal(string.Empty, EntityFolding.Fold(input));

    /// <summary>Media resolves stylised spellings before folding; a roster name does not.</summary>
    [Fact]
    public void FoldMedia_AStylisedSpelling_MatchesThePlainName()
    {
        Assert.Equal(EntityFolding.Fold("kesha"), EntityFolding.FoldMedia("Ke$ha"));
        Assert.NotEqual(EntityFolding.Fold("kesha"), EntityFolding.Fold("Ke$ha"));
    }
}
