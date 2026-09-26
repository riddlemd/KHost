using System.Runtime.InteropServices;
using HarfBuzzSharp;
using SkiaSharp;
using HBBuffer = HarfBuzzSharp.Buffer;

namespace KHost.Domain.Services.BurnIn;

/// <summary>Shapes a line of words through HarfBuzz into positioned glyphs.</summary>
/// <remarks>Skia's own text drawing neither joins Arabic letters nor orders a right-to-left run, so
/// a line is shaped whole and each syllable recovered from its glyphs' clusters — shaping per
/// syllable would break every join that crosses a syllable boundary.
///
/// <para>Not thread-safe: it rewrites one HarfBuzz buffer on every call and keeps its fonts in a
/// plain dictionary. One per painting thread.</para></remarks>
internal sealed class LyricTextShaper : IDisposable
{
    private readonly Dictionary<IntPtr, HarfBuzzSharp.Font> _fonts = [];
    private readonly HBBuffer _buffer = new();

    /// <summary>One glyph, positioned from the start of the run, and the character it came from.</summary>
    public readonly record struct Glyph(ushort Id, float X, float Y, float Advance, int Cluster);

    /// <summary>A shaped run: glyphs in visual (left to right) order, and its total advance.</summary>
    public readonly record struct Shaped(IReadOnlyList<Glyph> Glyphs, float Width);

    /// <param name="rightToLeft">Taken from the lyrics rather than guessed from the script: a song
    /// that says it reads right to left does so whatever letters it uses.</param>
    public Shaped Shape(SKTypeface typeface, string text, float sizePx, bool rightToLeft)
    {
        var font = FontFor(typeface);
        var scale = sizePx / typeface.UnitsPerEm;

        _buffer.ClearContents();
        _buffer.AddUtf16(text);
        _buffer.GuessSegmentProperties();
        _buffer.Direction = rightToLeft ? Direction.RightToLeft : Direction.LeftToRight;
        font.Shape(_buffer);

        var infos = _buffer.GlyphInfos;
        var positions = _buffer.GlyphPositions;
        var glyphs = new Glyph[infos.Length];
        float penX = 0;

        for (var i = 0; i < infos.Length; i++)
        {
            var advance = positions[i].XAdvance * scale;
            glyphs[i] = new Glyph(
                (ushort)infos[i].Codepoint,
                penX + positions[i].XOffset * scale,
                -positions[i].YOffset * scale,
                advance,
                (int)infos[i].Cluster);
            penX += advance;
        }

        return new Shaped(glyphs, penX);
    }

    public void Dispose()
    {
        _buffer.Dispose();
        foreach (var font in _fonts.Values) font.Dispose();
        _fonts.Clear();
    }

    private HarfBuzzSharp.Font FontFor(SKTypeface typeface)
    {
        if (_fonts.TryGetValue(typeface.Handle, out var existing)) return existing;

        using var asset = typeface.OpenStream(out var collectionIndex);
        var length = asset.Length;
        var data = new byte[length];
        asset.Read(data, length);

        // Pinned for as long as HarfBuzz holds the blob; the release callback unpins it.
        var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        var blob = new Blob(handle.AddrOfPinnedObject(), length, MemoryMode.Duplicate, () => handle.Free());
        var face = new Face(blob, (uint)collectionIndex) { UnitsPerEm = typeface.UnitsPerEm };

        var font = new HarfBuzzSharp.Font(face);
        font.SetScale(typeface.UnitsPerEm, typeface.UnitsPerEm);
        font.SetFunctionsOpenType();
        _fonts[typeface.Handle] = font;
        return font;
    }
}
