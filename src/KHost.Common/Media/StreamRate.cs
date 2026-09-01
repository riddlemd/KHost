using KHost.Abstractions.Models;

namespace KHost.Common.Media;

public static class StreamRate
{
    /// <summary>
    /// The one definition of what a tempo percentage means. Every consumer that keeps a clock —
    /// the host's, a screen's, a Cast receiver's — converts through it, and they must agree.
    /// </summary>
    public static double FromTempo(int tempo) => 1.0 + (tempo / 100.0);

    /// <summary>Multiplier the session's tempo stands for. Stream seconds times this are song seconds.</summary>
    public static double PlaybackRate(this MediaStreamSession session) => FromTempo(session.Tempo);
}
