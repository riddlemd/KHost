using KHost.Abstractions.Models;

namespace KHost.UserInterface.Models;

/// <summary>Shared by the two playlist managers, so a playlist reads the same on either page.</summary>
public static class PlaylistDisplay
{
    public static string DescribeSelection(MediaPool pool) => pool.SelectionMode switch
    {
        PoolSelectionMode.Sequential => "In order",
        PoolSelectionMode.Weighted => "By weight",
        _ => "Shuffled",
    };

    public static string DescribeEntries(MediaPool pool)
    {
        var nested = pool.Entries.Count(e => e.IsPool);
        var tracks = pool.Entries.Count - nested;

        return nested == 0
            ? $"{tracks}"
            : $"{tracks} + {nested} playlist{(nested == 1 ? "" : "s")}";
    }

    public static string DescribeTrigger(MediaPool pool) => pool.AdTrigger switch
    {
        AdTriggerMode.EveryNPerformances => $"Every {pool.AdTriggerInterval} songs",
        AdTriggerMode.EveryNMinutes => $"Every {pool.AdTriggerInterval} minutes",
        AdTriggerMode.OnIdle => "When nobody is queued",
        _ => "Only when asked",
    };
}
