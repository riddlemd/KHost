using KHost.Abstractions.Models;

namespace KHost.UnitTests.Domain.Models;

public class PerformanceTests
{
    [Fact]
    public void Performance_InitializesWithNewGuidId()
    {
        var performance = new Performance();

        Assert.NotEqual(Guid.Empty, performance.Id);
    }

    [Fact]
    public void Performance_TwoInstancesHaveDistinctIds()
    {
        var a = new Performance();
        var b = new Performance();

        Assert.NotEqual(a.Id, b.Id);
    }

    [Fact]
    public void Performance_DefaultQueuePositionIsNull()
    {
        var performance = new Performance();

        Assert.Null(performance.QueuePosition);
    }

    [Fact]
    public void Performance_SingerIdDefaultsToEmpty()
    {
        var performance = new Performance();

        Assert.Equal(Guid.Empty, performance.SingerId);
    }

    [Fact]
    public void Performance_MediaIdDefaultsToEmpty()
    {
        var performance = new Performance();

        Assert.Equal(Guid.Empty, performance.MediaId);
    }
}
