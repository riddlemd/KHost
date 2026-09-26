namespace KHost.Domain.Services.BurnIn;

/// <summary>What the burned-in words are laid over.</summary>
internal enum BurnInBase
{
    /// <summary>The source's own moving picture, fitted inside the frame.</summary>
    SourceVideo,

    /// <summary>One of the venue's song backgrounds, looped and cropped to cover the frame.</summary>
    Background,

    /// <summary>Plain black, which is what the screen shows behind a song with no picture.</summary>
    Fill,
}

/// <summary>The painted frames an encode reads off its pipe, and the picture they go over.</summary>
/// <param name="BackgroundPath">The clip to loop; set only for <see cref="BurnInBase.Background"/>.</param>
internal sealed record BurnInOverlay(int Width, int Height, int FramesPerSecond, BurnInBase Base, string? BackgroundPath = null)
{
    /// <summary>720p: the frame when graphics scaling is off, since painted words always need a
    /// frame of their own.</summary>
    public const int DefaultWidth = 1280;

    /// <inheritdoc cref="DefaultWidth"/>
    public const int DefaultHeight = 720;

    /// <summary>The largest frame words are painted into, whatever the graphics scaling asks for.</summary>
    /// <remarks>A .cdg seeks on its output, so a reopen mid-song paints and discards every frame
    /// before the playhead. At 1080p a reopen two minutes in took 13 seconds to its first segment
    /// and sometimes outran the playlist timeout; 4K could not keep up with the song at all.</remarks>
    public const int MaxHeight = 720;

    /// <summary>The rate words are painted at, and so the rate the picture under them is brought to.</summary>
    public const int DefaultFramesPerSecond = 30;

    /// <summary>The frame every burned-in stream is painted at: the graphics scaling's own frame up
    /// to <see cref="MaxHeight"/>, and 720p when scaling is off.</summary>
    public static (int Width, int Height) FrameFor(int graphicsHeight)
        => graphicsHeight <= GraphicsScaling.Off
            ? (DefaultWidth, DefaultHeight)
            : GraphicsScaling.FrameOfHeight(Math.Min(graphicsHeight, MaxHeight));
}

/// <summary>Everything one burned-in encode needs to feed its pipe.</summary>
internal sealed record BurnInPlan(
    BurnInOverlay Overlay, TimedLyricsPainter Painter, double StartSeconds, double Rate, int FrameCount, int Workers);
