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




}
