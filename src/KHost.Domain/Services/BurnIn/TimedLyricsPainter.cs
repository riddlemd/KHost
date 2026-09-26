using System.Runtime.InteropServices;
using KHost.Abstractions.Models;
using SkiaSharp;

namespace KHost.Domain.Services.BurnIn;

/// <summary>Paints one frame of a song's <see cref="TimedLyrics"/> at a moment in the song: the
/// words and their chase, count-ins and lead-ins, onto a transparent frame an encode lays over the
/// picture.</summary>
/// <remarks>The same rules the local screen draws by (<c>screen-ui/lyrics-overlay.js</c>), so a song
/// reads alike whichever display it reaches: the page fitted and centred in the frame, colours the
/// timing leaves unset taken from the same theme, the wipe linear with no easing, a count-in that
/// eases over one step and clears as the next page arrives, and a lead-in running to the line's
/// leading edge. Where the two part company it is because a web view cannot do better: this shapes
/// each line through HarfBuzz, so a joined script joins, and a right-to-left line is laid from the
/// right edge of its box.
///
/// <para>Knows nothing of ffmpeg or processes; <see cref="BurnInFramePump"/> feeds its frames to an
/// encode. Safe to share between threads — every mutable thing lives on a <see cref="Worker"/>.</para>
/// </remarks>
public sealed class TimedLyricsPainter
{
    /// <summary>The screen's theme colour for words already sung, when the timing names none.</summary>
    internal static readonly SKColor ThemeActive = new(0x85, 0x58, 0xFA);

    /// <summary>The theme colour for words not yet reached, when the timing names none.</summary>
    internal static readonly SKColor ThemeInactive = SKColors.White;

    /// <summary>The outline under every word, the lead-in's edge and the countdown's.</summary>
    internal static readonly SKColor OutlineColor = new(0, 0, 0, 217);

    /// <summary>How long a count-in takes to clear once a page arrives inside its window.</summary>
    /// <remarks>A timing brings the next page up a beat before the gap ends, often over the bar's own
    /// spot, and a singer pre-reads the first line as it lands — so the bar is gone by the page's
    /// arrival, not after it.</remarks>
    internal const double HandoverSeconds = 0.5;

    // Where a line with no position of its own goes, in the timing's units: the screen's defaults.
    private const double DefaultLineX = 40, DefaultLineY = 40, DefaultLineWidth = 520, DefaultLineHeight = 48;

    private readonly TimedLyrics _lyrics;
    private readonly bool _scrim;
    private readonly float _scale, _offsetX, _offsetY;
    private readonly double?[] _handovers;
    private readonly LyricBox[][] _lineBoxes;

    /// <param name="width">Frame width in pixels.</param>
    /// <param name="height">Frame height in pixels.</param>
    /// <param name="scrim">Whether to darken the band the words sit in, for words laid over a
    /// picture that was not made with them in mind.</param>
    public TimedLyricsPainter(TimedLyrics lyrics, int width, int height, bool scrim = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        _lyrics = lyrics;
        _scrim = scrim;
        Width = width;
        Height = height;

        // The whole logical page fitted, not just its height: a frame is whatever shape the encode
        // is, and scaling by height alone on a narrow one runs the words off the side.
        var logicalWidth = lyrics.Bounds.Width > 0 ? lyrics.Bounds.Width : 640;
        var logicalHeight = lyrics.Bounds.Height > 0 ? lyrics.Bounds.Height : 360;
        _scale = (float)Math.Min(width / logicalWidth, height / logicalHeight);
        _offsetX = (float)((width - logicalWidth * _scale) / 2);
        _offsetY = (float)((height - logicalHeight * _scale) / 2);

        _handovers = [.. lyrics.CountIns.Select(HandoverAt)];
        _lineBoxes = [.. lyrics.Pages.Select(page => LineBoxes(page.Lines))];
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>How long there is anything to paint: the timing's own length, or its last word or
    /// count-in when one runs past it.</summary>
    public double DurationSeconds
        => new[]
        {
            _lyrics.DurationSeconds,
            _lyrics.Pages.Select(page => page.ShowUntilSeconds).DefaultIfEmpty(0).Max(),
            _lyrics.CountIns.Select(countIn => countIn.EndSeconds).DefaultIfEmpty(0).Max(),
        }.Max();

    /// <summary>A frame buffer this painter paints into: <see cref="Width"/> by <see cref="Height"/>,
    /// four bytes a pixel, as ffmpeg's <c>-pix_fmt rgba</c> reads them.</summary>
    public Frame CreateFrame()
    {
        var info = new SKImageInfo(Width, Height, SKColorType.Rgba8888, FrameAlphaType);
        var pixels = new byte[info.RowBytes * Height];
        var pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        var surface = SKSurface.Create(info, pin.AddrOfPinnedObject(), info.RowBytes);

        if (surface is null)
        {
            pin.Free();
            throw new InvalidOperationException("Skia would not paint onto the frame buffer.");
        }

        return new Frame(pixels, pin, surface);
    }

    /// <summary>Per-thread painting state. Painting in parallel needs one per thread.</summary>
    public Worker CreateWorker() => new(this);

    /// <summary>Straight alpha, because the frames are laid over a picture and ffmpeg reads
    /// <c>rgba</c> as straight.</summary>
    /// <remarks>Premultiplied, a half-covered white glyph edge would arrive as grey at half alpha and
    /// composite at a quarter of its brightness: a dirty rim round every word.</remarks>
    internal static SKAlphaType FrameAlphaType => SKAlphaType.Unpremul;

    /// <summary>Linear, clamped to 0..1: where <paramref name="t"/> sits between two song positions.</summary>
    internal static double Progress(double t, double from, double to)
        => to <= from ? (t >= from ? 1 : 0) : Math.Clamp((t - from) / (to - from), 0, 1);

    /// <summary>Where each line of a page sits, in the timing's units.</summary>
    /// <remarks>A line with no position stacks directly under the line before it, or at the screen's
    /// default spot when it is the first — the contract's "let the screen stack it".</remarks>
    internal static LyricBox[] LineBoxes(IReadOnlyList<LyricLine> lines)
    {
        var boxes = new LyricBox[lines.Count];

        for (var i = 0; i < lines.Count; i++)
        {
            boxes[i] = lines[i].Position
                ?? new LyricBox(
                    DefaultLineX,
                    i == 0 ? DefaultLineY : boxes[i - 1].Y + boxes[i - 1].Height,
                    DefaultLineWidth,
                    DefaultLineHeight);
        }

        return boxes;
    }

    /// <summary>When the first page to arrive inside a count-in's window shows, or null.</summary>
    private double? HandoverAt(LyricCountIn countIn)
    {
        double? at = null;

        foreach (var page in _lyrics.Pages)
        {
            var from = page.ShowFromSeconds;
            if (from > countIn.StartSeconds && from < countIn.EndSeconds && (at is null || from < at)) at = from;
        }

        return at;
    }

    private static SKColor ColorOf(LyricColor? color, SKColor fallback)
        => color is null ? fallback : new SKColor(color.R, color.G, color.B);

    private void PaintFrame(Worker worker, SKCanvas canvas, double t)
    {
        canvas.Clear(SKColors.Transparent);
        if (_scrim) PaintScrim(canvas, Width, Height);

        for (var i = 0; i < _lyrics.CountIns.Count; i++) PaintCountIn(worker, canvas, i, t);

        for (var i = 0; i < _lyrics.Pages.Count; i++)
        {
            var page = _lyrics.Pages[i];
            if (t < page.ShowFromSeconds || t > page.ShowUntilSeconds) continue;

            var layout = worker.Layouts[i] ??= LayoutPage(worker, i);
            foreach (var line in layout.Lines) PaintLine(worker, canvas, layout, line, t);
        }
    }

    /// <summary>Darkens the band the words sit in, so whatever picture is under them stays readable.
    /// </summary>
    /// <remarks>Measured over a loop whose bright areas drift through the lower third: the worst pixel
    /// behind the words ran to 139 of 235 without it and 88 with it, while the band's average moved
    /// only 23 to 19, so the picture above the words is left alone.</remarks>
    internal static void PaintScrim(SKCanvas canvas, int width, int height)
    {
        var top = height * 0.55f;
        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(0, top), new SKPoint(0, height),
            [new SKColor(0, 0, 0, 0), new SKColor(0, 0, 0, 140), new SKColor(0, 0, 0, 204)],
            [0f, 0.51f, 1f],
            SKShaderTileMode.Clamp);
        using var paint = new SKPaint { Shader = shader };
        canvas.DrawRect(new SKRect(0, top, width, height), paint);
    }

    /// <summary>The bar across a gap: eased in and out over one step, filled and counted down over
    /// its last <see cref="LyricCountIn.Steps"/> steps to the page arriving inside it, or to its end
    /// when none does.</summary>
    private void PaintCountIn(Worker worker, SKCanvas canvas, int index, double t)
    {
        var countIn = _lyrics.CountIns[index];
        if (t < countIn.StartSeconds || t >= countIn.EndSeconds) return;

        var handover = _handovers[index];

        // Counted to the page rather than the first word: the bar is gone by then, so a count to the
        // word would never show its 1. The screen's overlay counts by the same rule.
        var countTo = handover ?? countIn.EndSeconds;
        var leaving = handover is { } at ? 1 - Progress(t, at - HandoverSeconds, at) : 1;
        if (leaving <= 0) return;

        var step = countIn.StepSeconds;
        var alpha = Math.Min(leaving, step > 0
            ? Math.Min(1, Math.Min((t - countIn.StartSeconds) / step, (countIn.EndSeconds - t) / step))
            : 1);

        var box = countIn.Position;
        var x = _offsetX + (float)box.X * _scale;
        var y = _offsetY + (float)box.Y * _scale;
        var w = (float)box.Width * _scale;
        var h = (float)box.Height * _scale;
        var fill = Progress(t, countIn.StartSeconds, countTo);
        var bar = new SKRoundRect(new SKRect(x, y, x + w, y + h), 4 * _scale);

        using (var layer = new SKPaint { Color = SKColors.White.WithAlpha(ToByte(alpha)) })
        {
            canvas.SaveLayer(layer);

            using (var inactive = new SKPaint { Color = ColorOf(countIn.Inactive, ThemeInactive), IsAntialias = true })
                canvas.DrawRoundRect(bar, inactive);

            canvas.Save();
            canvas.ClipRoundRect(bar, antialias: true);
            using (var active = new SKPaint { Color = ColorOf(countIn.Active, ThemeActive) })
                canvas.DrawRect(new SKRect(x, y, x + w * (float)fill, y + h), active);
            canvas.Restore();

            if (countIn.BorderWidth > 0)
            {
                using var border = new SKPaint
                {
                    Color = ColorOf(countIn.Border, SKColors.Black),
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = (float)countIn.BorderWidth * _scale,
                };
                canvas.DrawRoundRect(bar, border);
            }

            canvas.Restore();
        }

        // Not eased with the bar — the last number is the one that must be read — but it still
        // leaves with the handover, or its digits land on the page's first line.
        if (step <= 0 || countIn.Steps <= 0 || t < countTo - countIn.Steps * step) return;

        var n = Math.Min(countIn.Steps, (int)Math.Floor((countTo - t) / step) + 1);
        var font = worker.Font(LyricFonts.Countdown, h * 1.6f);
        var text = n.ToString(System.Globalization.CultureInfo.InvariantCulture);

        using var stroke = new SKPaint
        {
            Color = OutlineColor.WithAlpha(ToByte(OutlineColor.Alpha / 255.0 * leaving)),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = Math.Max(2, font.Size * 0.06f),
            StrokeJoin = SKStrokeJoin.Round,
        };
        using var digit = new SKPaint { Color = SKColors.White.WithAlpha(ToByte(leaving)), IsAntialias = true };

        canvas.DrawText(text, x + w / 2, y + h * 1.25f, SKTextAlign.Center, font, stroke);
        canvas.DrawText(text, x + w / 2, y + h * 1.25f, SKTextAlign.Center, font, digit);
    }

    private static byte ToByte(double fraction) => (byte)Math.Round(Math.Clamp(fraction, 0, 1) * 255);

    private PageLayout LayoutPage(Worker worker, int pageIndex)
    {
        var page = _lyrics.Pages[pageIndex];
        var boxes = _lineBoxes[pageIndex];
        var rtl = _lyrics.IsRightToLeft;
        var lines = new List<LineLayout>(page.Lines.Count);

        for (var i = 0; i < page.Lines.Count; i++)
        {
            var line = page.Lines[i];
            var box = boxes[i];
            var h = (float)box.Height * _scale;
            var boxWidth = (float)box.Width * _scale;
            var left = _offsetX + (float)box.X * _scale;
            var baseline = _offsetY + (float)box.Y * _scale + h * 0.8f;

            var text = new System.Text.StringBuilder();
            var ranges = new List<(int Start, int End, LyricSyllable Syllable)>();

            foreach (var syllable in line.Syllables)
            {
                // Spacing is the provider's: a syllable is as often part of a word as a whole one.
                if (syllable.Text.Length == 0) continue;
                var start = text.Length;
                text.Append(syllable.Text);
                ranges.Add((start, text.Length, syllable));
            }

            var fontSize = h * 0.82f;
            var syllables = new List<SyllableLayout>();
            SKFont? font = null;

            if (ranges.Count > 0)
            {
                var typeface = LyricFonts.For(text.ToString());
                var shaped = worker.Shaper.Shape(typeface, text.ToString(), fontSize, rtl);

                // Height alone sets the size, since the box was authored against some other face; the
                // box's width is what the line was promised, so a wider face is held to it.
                if (shaped.Width > boxWidth && boxWidth > 0)
                {
                    fontSize *= boxWidth / shaped.Width;
                    shaped = worker.Shaper.Shape(typeface, text.ToString(), fontSize, rtl);
                }

                font = worker.Font(typeface, fontSize);

                // A right-to-left line starts at its box's right edge, where its lead-in arrives.
                var penX = rtl ? left + Math.Max(boxWidth, shaped.Width) - shaped.Width : left;

                foreach (var (start, end, syllable) in ranges)
                {
                    var ids = new List<ushort>();
                    var points = new List<SKPoint>();
                    float lo = float.MaxValue, hi = float.MinValue;

                    foreach (var glyph in shaped.Glyphs)
                    {
                        if (glyph.Cluster < start || glyph.Cluster >= end) continue;
                        ids.Add(glyph.Id);
                        points.Add(new SKPoint(penX + glyph.X, baseline + glyph.Y));
                        lo = Math.Min(lo, penX + glyph.X);
                        hi = Math.Max(hi, penX + glyph.X + glyph.Advance);
                    }

                    if (ids.Count == 0) continue;

                    using var builder = new SKTextBlobBuilder();
                    var run = builder.AllocatePositionedRun(font, ids.Count);
                    ids.ToArray().AsSpan().CopyTo(run.Glyphs);
                    points.ToArray().AsSpan().CopyTo(run.Positions);

                    if (builder.Build() is { } blob) syllables.Add(new SyllableLayout(syllable, blob, lo, hi));
                }
            }

            lines.Add(new LineLayout(line, line.Position, [.. syllables], baseline, h, fontSize));
        }

        return new PageLayout(
            [.. lines],
            ColorOf(page.Active, ThemeActive),
            ColorOf(page.Inactive, ThemeInactive));
    }

    private void PaintLine(Worker worker, SKCanvas canvas, PageLayout page, LineLayout line, double t)
    {
        PaintLeadIn(canvas, page, line, t);

        // Every outline before any fill, so one syllable's edge never lands across its neighbour.
        worker.Outline.StrokeWidth = Math.Max(2, line.FontSize * 0.09f);
        foreach (var syllable in line.Syllables) canvas.DrawText(syllable.Blob, 0, 0, worker.Outline);

        worker.Fill.Color = page.Inactive;
        foreach (var syllable in line.Syllables) canvas.DrawText(syllable.Blob, 0, 0, worker.Fill);

        worker.Fill.Color = page.Active;
        foreach (var syllable in line.Syllables)
        {
            var frac = (float)Progress(t, syllable.Syllable.StartSeconds, syllable.Syllable.EndSeconds);
            if (frac <= 0) continue;

            // A clipped reveal over the words already painted, so the wipe can stop part way through
            // a letter. Right to left, it runs in from the syllable's right edge.
            var width = syllable.Right - syllable.Left;
            var (lo, hi) = _lyrics.IsRightToLeft
                ? (syllable.Right - width * frac, syllable.Right)
                : (syllable.Left, syllable.Left + width * frac);

            canvas.Save();
            canvas.ClipRect(new SKRect(lo, line.Baseline - line.Height, hi, line.Baseline + line.Height));
            canvas.DrawText(syllable.Blob, 0, 0, worker.Fill);
            canvas.Restore();
        }
    }

    /// <summary>A small block that travels in to the line's leading edge, arriving as its first
    /// syllable lights.</summary>
    private void PaintLeadIn(SKCanvas canvas, PageLayout page, LineLayout line, double t)
    {
        var leadIn = line.Source.LeadIn;
        var box = line.Position;
        var first = line.Source.Syllables.Count > 0 ? line.Source.Syllables[0] : null;
        if (leadIn is null || box is null || first is null || t < leadIn.StartSeconds || t >= first.StartSeconds) return;

        var run = box.X - leadIn.X;

        // Mirrored for right to left: the same run, made into the right edge from outside it.
        var from = _lyrics.IsRightToLeft ? box.X + box.Width + run : leadIn.X;
        var to = _lyrics.IsRightToLeft ? box.X + box.Width : box.X;
        var head = from + (to - from) * Progress(t, leadIn.StartSeconds, first.StartSeconds);

        var w = 10 * _scale;
        var h = (float)box.Height * 0.3f * _scale;
        var x = _offsetX + (float)head * _scale - w / 2;
        var y = line.Baseline - line.FontSize * 0.35f - h / 2;
        var rect = new SKRect(x, y, x + w, y + h);

        using var fill = new SKPaint { Color = page.Active };
        using var edge = new SKPaint { Color = OutlineColor, Style = SKPaintStyle.Stroke, StrokeWidth = 3 * _scale };
        canvas.DrawRect(rect, fill);
        canvas.DrawRect(rect, edge);
    }

    internal sealed record SyllableLayout(LyricSyllable Syllable, SKTextBlob Blob, float Left, float Right);

    /// <param name="Position">The line's own position; null for a stacked line, which has no lead-in.</param>
    internal sealed record LineLayout(
        LyricLine Source, LyricBox? Position, SyllableLayout[] Syllables, float Baseline, float Height, float FontSize);

    /// <summary>Where every glyph of a page sits, shaped once: only the wipe moves between frames.</summary>
    internal sealed record PageLayout(LineLayout[] Lines, SKColor Active, SKColor Inactive) : IDisposable
    {
        public void Dispose()
        {
            foreach (var line in Lines)
                foreach (var syllable in line.Syllables) syllable.Blob.Dispose();
        }
    }

    /// <summary>One frame's pixels, pinned, and the surface Skia paints straight into them.</summary>
    /// <remarks>Painted in place rather than snapshotted: a snapshot copies the whole frame every
    /// time, only to hand the pipe a picture already painted.</remarks>
    public sealed class Frame : IDisposable
    {
        private GCHandle _pin;

        internal Frame(byte[] pixels, GCHandle pin, SKSurface surface)
        {
            Pixels = pixels;
            _pin = pin;
            Surface = surface;
        }

        /// <summary>The frame as last painted, row by row, four bytes a pixel.</summary>
        public byte[] Pixels { get; }

        internal SKSurface Surface { get; }

        public void Dispose()
        {
            Surface.Dispose();
            if (_pin.IsAllocated) _pin.Free();
        }
    }

    /// <summary>Everything one painting thread needs, owned by that thread alone.</summary>
    /// <remarks>None of it may be shared: the shaper rewrites one HarfBuzz buffer per call, and the
    /// font cache, the brushes and the laid-out pages are plain mutable objects. Shared across
    /// threads they garble glyph runs rather than failing outright.</remarks>
    public sealed class Worker : IDisposable
    {
        private readonly TimedLyricsPainter _owner;
        private readonly Dictionary<(IntPtr, float), SKFont> _fonts = [];

        internal Worker(TimedLyricsPainter owner)
        {
            _owner = owner;
            Layouts = new PageLayout?[owner._lyrics.Pages.Count];
        }

        internal LyricTextShaper Shaper { get; } = new();

        internal SKPaint Fill { get; } = new() { IsAntialias = true };

        internal SKPaint Outline { get; } = new()
        {
            Color = OutlineColor,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            // Round, or the outline grows spikes off every sharp corner of a glyph.
            StrokeJoin = SKStrokeJoin.Round,
            StrokeCap = SKStrokeCap.Round,
        };

        /// <summary>Shaped pages by their place in the lyrics, filled as each first shows.</summary>
        internal PageLayout?[] Layouts { get; }

        /// <summary>Paints the song as it stands at <paramref name="songSeconds"/> into
        /// <paramref name="frame"/>, which must come from the same painter.</summary>
        public void Paint(Frame frame, double songSeconds)
        {
            Paint(frame.Surface.Canvas, songSeconds);
            frame.Surface.Flush();
        }

        public void Dispose()
        {
            foreach (var layout in Layouts) layout?.Dispose();
            foreach (var font in _fonts.Values) font.Dispose();
            Shaper.Dispose();
            Fill.Dispose();
            Outline.Dispose();
        }

        /// <summary>Paints onto any canvas of the painter's size, clearing it first.</summary>
        internal void Paint(SKCanvas canvas, double songSeconds) => _owner.PaintFrame(this, canvas, songSeconds);

        internal SKFont Font(SKTypeface typeface, float size)
        {
            var key = (typeface.Handle, size);
            if (!_fonts.TryGetValue(key, out var font)) _fonts[key] = font = new SKFont(typeface, size);
            return font;
        }
    }
}
