using KHost.Abstractions.Models;
using KHost.UserInterface.Models;

namespace KHost.UnitTests.UserInterface.Models;

public class PlaylistDisplayTests
{
    private static string Trigger(AdTriggerMode mode, int interval)
        => PlaylistDisplay.DescribeTrigger(new MediaPool { Name = "Spots", AdTrigger = mode, AdTriggerInterval = interval });

    [Theory]
    [InlineData(AdTriggerMode.EveryNPerformances, 1, "Every song")]
    [InlineData(AdTriggerMode.EveryNPerformances, 2, "Every 2 songs")]
    [InlineData(AdTriggerMode.EveryNMinutes, 1, "Every minute")]
    [InlineData(AdTriggerMode.EveryNMinutes, 15, "Every 15 minutes")]
    public void DescribeTrigger_ReadsAsEnglish(AdTriggerMode mode, int interval, string expected)
        => Assert.Equal(expected, Trigger(mode, interval));
}
