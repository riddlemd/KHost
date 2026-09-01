using KHost.Abstractions.Services;
using KHost.Common.Orthography;

namespace KHost.UnitTests.DataAccess.Contexts;

/// <summary>
/// Folding is offered to plugins through DI so the transliteration package stays host-side. What
/// matters is that what a plugin gets is what the host stored — these pin the behaviour a plugin
/// would be relying on.
/// </summary>
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

    /// <summary>
    /// The media rule is composed, not built in: a plugin resolves stylised spellings from
    /// KHost.Common first, then folds. Pinning it here keeps that recipe honest.
    /// </summary>
    [Fact]
    public void Fold_AfterResolvingAStylisedSpelling_MatchesThePlainName()
        => Assert.Equal(_folding.Fold("kesha"), _folding.Fold(StylisedSpelling.ResolveToPlainSpelling("Ke$ha")));

    // The implementation is internal — a plugin resolves it from DI rather than naming the type,
    // which is the whole point of the interface.
    private static ITextFolding Build()
    {
        var type = typeof(KHost.DataAccess.DatabaseLocation).Assembly
            .GetType("KHost.DataAccess.Contexts.TextFolding")!;

        return (ITextFolding)Activator.CreateInstance(type, nonPublic: true)!;
    }
}
