using KHost.Abstractions.Models;

namespace KHost.Domain.Services;

/// <summary>What the main channel is carrying, as facts a display pictures for itself.</summary>
/// <remarks>Domain-side for now: it says what is on, never how to show it. Compared by value, so a
/// display redraws when the program moves and not on every PlaybackChanged.</remarks>
public abstract record PlaybackProgram
{
    /// <summary>Nothing on the main channel. An audio-only ad counts: it has no picture of its own.</summary>
    public sealed record Idle : PlaybackProgram;

    /// <summary>A song, or a video ad when <paramref name="Performance"/> is null.</summary>
    public sealed record Playing(Media Media, Performance? Performance) : PlaybackProgram;

    /// <summary>An ad that is a picture rather than a stream, run out by the host's own clock.</summary>
    public sealed record AdStill(string ImageUrl, ImageScaling Scaling) : PlaybackProgram;
}

/// <summary>Where a display reads the current program, without taking the whole playback service.</summary>
public interface IPlaybackProgram
{
    PlaybackProgram CurrentProgram { get; }
}
