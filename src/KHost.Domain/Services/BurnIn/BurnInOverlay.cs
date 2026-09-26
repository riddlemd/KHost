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
    /// <summary>720p: sharp words on a television without painting more pixels than an encode that
    /// has to stay ahead of the song can afford. Every burned-in stream is this size.</summary>
    public const int DefaultWidth = 1280;

    /// <inheritdoc cref="DefaultWidth"/>
    public const int DefaultHeight = 720;

    /// <summary>The rate words are painted at, and so the rate the picture under them is brought to.</summary>
    public const int DefaultFramesPerSecond = 30;
}

/// <summary>Everything one burned-in encode needs to feed its pipe.</summary>
internal sealed record BurnInPlan(
    BurnInOverlay Overlay, TimedLyricsPainter Painter, double StartSeconds, double Rate, int FrameCount, int Workers);
