using KHost.Abstractions.Models;

namespace KHost.Common.Media;

/// <summary>Converts a session's tempo percentage to the playback speed multiplier it stands for.</summary>
public static class StreamRate
{
    /// <summary>The one definition of what a tempo percentage means.</summary>
    /// <remarks>Every clock-keeping consumer converts through it, and they must agree.</remarks>
    public static double FromTempo(int tempo) => 1.0 + (tempo / 100.0);

    /// <summary>Multiplier the session's tempo stands for.</summary>
    /// <remarks>Stream seconds times this are song seconds.</remarks>
    public static double PlaybackRate(this MediaStreamSession session) => FromTempo(session.Tempo);
}
