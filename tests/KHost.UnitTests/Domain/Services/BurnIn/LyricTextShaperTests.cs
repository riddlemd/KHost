using KHost.Domain.Services.BurnIn;

namespace KHost.UnitTests.Domain.Services.BurnIn;

/// <summary>A line is shaped whole through HarfBuzz, so joined scripts join and a right-to-left run
/// comes back in visual order.</summary>
/// <remarks>Each line is shaped in the face the painter would pick for it (<see cref="LyricFonts.For"/>),
/// so the assertions hold whichever system face that is; none compares against a font's own metrics.</remarks>
public sealed class LyricTextShaperTests : IDisposable
{
    private const float Size = 40;

    private readonly LyricTextShaper _shaper = new();

    public void Dispose() => _shaper.Dispose();

    [Fact]
    public void Shape_Latin_IsOneGlyphPerLetterLaidLeftToRightFromZero()
    {
        var shaped = Shape("Hello");

        Assert.Equal([0, 1, 2, 3, 4], shaped.Glyphs.Select(g => g.Cluster));
        Assert.All(shaped.Glyphs, g => Assert.NotEqual(0, g.Id));
        Assert.Equal(0, shaped.Glyphs[0].X);

        for (var i = 1; i < shaped.Glyphs.Count; i++)
            Assert.Equal(shaped.Glyphs[i - 1].X + shaped.Glyphs[i - 1].Advance, shaped.Glyphs[i].X, precision: 3);

        Assert.Equal(shaped.Glyphs.Sum(g => g.Advance), shaped.Width, precision: 3);
    }

    /// <summary>Advances come back in pixels at the size asked for, not in the face's own units.</summary>
    [Fact]
    public void Shape_TwiceTheSize_IsTwiceTheWidth()
    {
        var small = Shape("Hello", size: 20).Width;
        var large = Shape("Hello", size: 40).Width;

        Assert.InRange(small, 20, 200);
        Assert.Equal(small * 2, large, precision: 2);
    }

    /// <summary>The same letter takes a different form at each end of a word; unjoined, all three are
    /// one glyph. Some faces draw the initial and medial forms alike, so only the ends are compared.</summary>
    [Fact]
    public void Shape_ArabicWord_JoinsEachLetterToItsNeighbours()
    {
        var shaped = Shape("ببب", rightToLeft: true);

        Assert.Equal(3, shaped.Glyphs.Count);
        Assert.All(shaped.Glyphs, g => Assert.NotEqual(0, g.Id));
        Assert.NotEqual(shaped.Glyphs[0].Id, shaped.Glyphs[^1].Id);
    }

    /// <summary>Lam followed by alef is one ligature in every Arabic face.</summary>
    [Fact]
    public void Shape_ArabicLamAlef_IsFewerGlyphsThanCodePoints()
    {
        const string salaam = "سلام";

        var shaped = Shape(salaam, rightToLeft: true);

        Assert.InRange(shaped.Glyphs.Count, 1, salaam.Length - 1);
        Assert.All(shaped.Glyphs, g => Assert.NotEqual(0, g.Id));
    }

    /// <summary>Glyphs are in visual order, so the line's first letter is its rightmost glyph.</summary>
    [Fact]
    public void Shape_HebrewRightToLeft_LaysTheFirstLetterAtTheRight()
    {
        var shaped = Shape("שלום", rightToLeft: true);

        Assert.Equal([3, 2, 1, 0], shaped.Glyphs.Select(g => g.Cluster));
        Assert.Equal(0, shaped.Glyphs[^1].Cluster);
        Assert.True(shaped.Glyphs[^1].X > shaped.Glyphs[0].X);
    }

    /// <summary>The direction is the lyrics' to give, not guessed from the letters.</summary>
    [Fact]
    public void Shape_HebrewMarkedLeftToRight_KeepsLogicalOrder()
        => Assert.Equal(
            [0, 1, 2, 3],
            Shape("שלום", rightToLeft: false).Glyphs.Select(g => g.Cluster));

    /// <summary>A combining accent stays with its letter and adds no width of its own.</summary>
    [Fact]
    public void Shape_CombiningMark_StaysInItsBaseLettersClusterWithoutWidening()
    {
        // No precomposed q-acute exists, so the mark stays a glyph of its own.
        var plain = Shape("q");
        var accented = Shape("q́");

        Assert.Equal(2, accented.Glyphs.Count);
        Assert.All(accented.Glyphs, g => Assert.Equal(0, g.Cluster));
        Assert.Equal(plain.Width, accented.Width, precision: 3);
    }

    [Fact]
    public void Shape_Empty_IsNoGlyphsAndNoWidth()
    {
        var shaped = Shape("");

        Assert.Empty(shaped.Glyphs);
        Assert.Equal(0, shaped.Width);
    }

    private LyricTextShaper.Shaped Shape(string text, bool rightToLeft = false, float size = Size)
        => _shaper.Shape(LyricFonts.For(text), text, size, rightToLeft);
}
