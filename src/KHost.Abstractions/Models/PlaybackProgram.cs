namespace KHost.Abstractions.Models;

/// <summary>What the main channel is carrying, as facts a display pictures for itself.</summary>
/// <remarks>It says what is on, never how to show it. Read from
/// <see cref="KHost.Abstractions.Services.IPlaybackService.CurrentProgram"/>, and announced by
/// <see cref="KHost.Abstractions.Messaging.Messages.PlaybackChanged"/> — which also fires for a
/// pause, a seek or a change of key, so compare by value and redraw only when the program moved.
/// Exactly one of the three cases below is ever current.</remarks>
public abstract record PlaybackProgram
{
    /// <summary>Nothing on the main channel. An audio-only ad counts: it has no picture of its own.</summary>
    public sealed record Idle : PlaybackProgram;

    /// <summary>A song, or a video ad when <paramref name="Performance"/> is null.</summary>
    /// <param name="Media">What is loaded on the main channel.</param>
    /// <param name="Performance">The singer's turn it belongs to; null for a video ad, which is
    /// nobody's song and has no words.</param>
    public sealed record Playing(Media Media, Performance? Performance) : PlaybackProgram;

    /// <summary>An ad that is a picture rather than a stream, shown until the program moves on.</summary>
    /// <param name="ImageUrl">Where the picture is fetched from. It is served on the same base
    /// address, and answers off this machine the same way, as every stream URL the host hands a
    /// display. Like a stream URL it may name the loopback host, so a provider whose device is
    /// elsewhere on the network substitutes an address that device reaches the host on, exactly
    /// as it does for a stream.</param>
    /// <param name="Scaling">How the picture fills the frame.</param>
    public sealed record AdStill(string ImageUrl, ImageScaling Scaling) : PlaybackProgram;
}
