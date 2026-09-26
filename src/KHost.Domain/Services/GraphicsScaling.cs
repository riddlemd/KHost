namespace KHost.Domain.Services;

/// <summary>How far a graphics-only source (a .cdg) is scaled up before it reaches a display, named
/// by the height of the picture it becomes.</summary>
/// <remarks>Scaled here, on whole pixels, because a display left to do it smears the blocks, and an
/// encode at the native 300x216 halves the colour resolution of a picture that is nothing but hard
/// colour edges.</remarks>
public static class GraphicsScaling
{
    /// <summary>The native picture, unscaled.</summary>
    public const int Off = 0;

    /// <summary>Native until a host opts in: scaling costs encode time on every CD+G song.</summary>
    public const int DefaultHeight = Off;

    /// <summary>The heights offered, smallest first. Each has been measured to encode at least three
    /// times faster than the song plays, tempo filter included.</summary>
    public static readonly IReadOnlyList<int> Heights = [Off, 720, 1080, 2160];

    /// <summary>What a .cdg always decodes to.</summary>
    internal const int SourceWidth = 300;

    /// <inheritdoc cref="SourceWidth"/>
    internal const int SourceHeight = 216;

    /// <summary>The largest offered height not above <paramref name="height"/>; below every one is
    /// <see cref="Off"/>.</summary>
    /// <remarks>Read as well as saved: a hand-edited height the list does not offer must still
    /// name a frame the encode knows how to fill.</remarks>
    public static int SnapToOffered(int height) => Heights.LastOrDefault(offered => offered <= height);

    /// <summary>The native picture's own shape at a given height, its width rounded to the nearest even number.</summary>
    /// <remarks>No bands: a picture padded out to 16:9 is fitted by the display as a whole, so the
    /// graphics end up smaller than the native picture the display would have stretched to the
    /// height. Even, because H.264 at 4:2:0 cannot encode an odd width.</remarks>
    public static (int Width, int Height) PictureOfHeight(int height)
        => ((int)Math.Round(height * SourceWidth / (double)SourceHeight / 2) * 2, height);

    /// <summary>The 16:9 frame of a given height.</summary>
    public static (int Width, int Height) FrameOfHeight(int height) => (height * 16 / 9, height);

    /// <summary>The largest whole-pixel multiple of the native picture that fits the frame.</summary>
    /// <remarks>Whole pixels only: a fractional nearest-neighbour scale draws some blocks a pixel
    /// wider than their neighbours, which reads as wobbling text.</remarks>
    internal static int WholeScaleFor(int width, int height)
        => Math.Max(1, Math.Min(width / SourceWidth, height / SourceHeight));
}
