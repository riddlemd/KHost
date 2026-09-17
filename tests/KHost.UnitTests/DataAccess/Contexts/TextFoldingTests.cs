using KHost.Abstractions.Services;
using KHost.Common.Orthography;

namespace KHost.UnitTests.DataAccess.Contexts;

/// <summary>Folding is offered to plugins via DI, so the transliteration package stays host-side.</summary>
public class TextFoldingTests
{
    private readonly ITextFolding _folding = Build();

    [Theory]
    [InlineData("Beyoncé", "beyonce")]
    [InlineData("  Björk  ", "bjork")]
    [InlineData("AC/DC", "ac/dc")]
    public void Fold_StripsAccentsAndCase(string input, string expected)
        => Assert.Equal(expected, _folding.Fold(input));

    /// <summary>Never null, so a folded value is always safe to compare against a stored one.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Fold_NothingIn_IsEmptyNotNull(string? input)
        => Assert.Equal(string.Empty, _folding.Fold(input));

    /// <summary>The rule is composed: KHost.Common resolves stylised spellings first, then folds.</summary>
    [Fact]
    public void Fold_AfterResolvingAStylisedSpelling_MatchesThePlainName()
        => Assert.Equal(_folding.Fold("kesha"), _folding.Fold(StylisedSpelling.ResolveToPlainSpelling("Ke$ha")));

    // The implementation is internal, so a plugin resolves it from DI rather than naming the type,
    // which is the whole point of the interface.
    private static ITextFolding Build()
    {
        var type = typeof(KHost.DataAccess.DatabaseLocation).Assembly
            .GetType("KHost.DataAccess.Contexts.TextFolding")!;

        return (ITextFolding)Activator.CreateInstance(type, nonPublic: true)!;
    }
}
