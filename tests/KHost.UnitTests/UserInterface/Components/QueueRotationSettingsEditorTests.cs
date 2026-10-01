using Bunit;
using KHost.Abstractions.Models;
using KHost.Abstractions.Models.QueueRotation;
using KHost.Abstractions.Services;
using KHost.Abstractions.Services.QueueRotation;
using KHost.UserInterface.Components;
using Microsoft.Extensions.DependencyInjection;

namespace KHost.UnitTests.UserInterface.Components;

/// <summary>The bounds match what VenuesService clamps the rotation's numbers to on save.</summary>
public class QueueRotationSettingsEditorTests : BunitContext
{
    public QueueRotationSettingsEditorTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var strategies = Substitute.For<IQueueRotationStrategyFactory>();
        strategies.GetAllModes().Returns([]);
        Services.AddSingleton(strategies);

        var userGroups = Substitute.For<IUserGroupsService>();
        userGroups.ReadAllAsync(1, Arg.Any<int>())
            .Returns(new PaginatedResult<KHostUserGroup> { Items = [], TotalCount = 0, PageNumber = 1, PageSize = 1000 });
        Services.AddSingleton(userGroups);
    }

    [Theory]
    [InlineData("#drop-index", "0", "100")]
    [InlineData("#boost-slots", "1", "20")]
    [InlineData("#cooldown", "0", "20")]
    public void FifoNumbers_CarryTheirBounds(string selector, string min, string max)
    {
        var cut = Render<QueueRotationSettingsEditor>(ps => ps.Add(p => p.Config, new QueueRotationConfig
        {
            StrategyId = "fifo",
            DropPosition = DropPositionMode.FixedIndex,
            FirstTimeBoostEnabled = true,
        }));

        var input = cut.Find(selector);

        Assert.Equal(min, input.GetAttribute("min"));
        Assert.Equal(max, input.GetAttribute("max"));
    }

    [Theory]
    [InlineData("#wait-weight")]
    [InlineData("#song-weight")]
    public void Weights_CarryTheirBounds(string selector)
    {
        var cut = Render<QueueRotationSettingsEditor>(ps => ps.Add(p => p.Config, new QueueRotationConfig
        {
            StrategyId = "weighted-fair",
        }));

        var input = cut.Find(selector);

        Assert.Equal("0", input.GetAttribute("min"));
        Assert.Equal("10", input.GetAttribute("max"));
    }
}
