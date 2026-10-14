using Microsoft.Extensions.Time.Testing;

namespace KHost.UnitTests;

internal static class FakeTimeProviderExtensions
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    /// <summary>Steps the clock until <paramref name="done"/> holds; false if it never does.</summary>
    /// <remarks>For work that reaches its timer from a pool thread: a step taken before the timer is
    /// armed moves nothing, so the clock is stepped again until the result shows.</remarks>
    public static async Task<bool> AdvanceUntilAsync(
        this FakeTimeProvider time, TimeSpan step, Func<bool> done, TimeSpan? patience = null)
    {
        var deadline = DateTime.UtcNow + (patience ?? Patience);

        while (!done())
        {
            if (DateTime.UtcNow >= deadline) return false;

            time.Advance(step);
            await Task.Delay(1);
        }

        return true;
    }

    /// <summary>Steps the clock <paramref name="steps"/> times, so each timer a step fires can arm the next.</summary>
    public static void Advance(this FakeTimeProvider time, TimeSpan step, int steps)
    {
        for (var i = 0; i < steps; i++)
            time.Advance(step);
    }
}
