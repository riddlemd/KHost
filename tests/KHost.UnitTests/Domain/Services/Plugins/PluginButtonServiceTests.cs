using KHost.Abstractions.Models.Plugins;
using KHost.Abstractions.Services;
using KHost.Domain.Services.Plugins;

namespace KHost.UnitTests.Domain.Services.Plugins;

public class PluginButtonServiceTests
{
    private const string PluginId = "0b000000-0000-4000-8000-00000000b001";

    private readonly IPluginButtonHandler _handler = Substitute.For<IPluginButtonHandler>();
    private readonly IPluginRegistry _registry = Substitute.For<IPluginRegistry>();

    public PluginButtonServiceTests()
        => _handler.DescribeButton(Arg.Any<string>()).Returns(PluginButtonState.Default);

    private PluginButtonService Service(params PluginButtonDefinition[] buttons)
    {
        _registry.Plugins.Returns(new List<DiscoveredPlugin>
        {
            new()
            {
                Directory = "/plugins/x",
                Manifest = new PluginManifest
                {
                    Id = Guid.Parse(PluginId),
                    Name = "X",
                    Version = "1.0.0",
                    EntryAssembly = "X.dll",
                    ApiVersion = PluginApi.CurrentVersion,
                    Buttons = [.. buttons],
                },
            },
        });
        return new PluginButtonService([new PluginButtonBinding(PluginId, _handler)], _registry);
    }

    private static PluginButtonDefinition Button(string key, string label = "Do it")
        => new() { Key = key, Label = label };

    [Fact]
    public void ButtonsFor_ReturnsTheManifestButtonsInOrder_WithTheirState()
    {
        var service = Service(Button("a"), Button("b"));

        var buttons = service.ButtonsFor(PluginId);

        Assert.Equal(["a", "b"], buttons.Select(x => x.Definition.Key));
    }

    [Fact]
    public void ButtonsFor_UsesTheHandlersLabelOverride()
    {
        _handler.DescribeButton("a").Returns(new PluginButtonState { Label = "Sign out" });
        var service = Service(Button("a", "Sign in"));

        Assert.Equal("Sign out", Assert.Single(service.ButtonsFor(PluginId)).State.Label);
    }

    [Fact]
    public void ButtonsFor_LeavesOutAHiddenButton()
    {
        _handler.DescribeButton("hidden").Returns(new PluginButtonState { Visible = false });
        var service = Service(Button("shown"), Button("hidden"));

        Assert.Equal(["shown"], service.ButtonsFor(PluginId).Select(x => x.Definition.Key));
    }

    [Fact]
    public void ButtonsFor_UnknownPlugin_IsEmpty()
        => Assert.Empty(Service(Button("a")).ButtonsFor("no-such-plugin"));

    /// <summary>A plugin whose DescribeButton throws must not take the whole row's buttons down.</summary>
    [Fact]
    public void ButtonsFor_HandlerThrows_FallsBackToTheDefaultState()
    {
        _handler.DescribeButton("a").Returns(_ => throw new InvalidOperationException("boom"));
        var service = Service(Button("a", "Label"));

        var button = Assert.Single(service.ButtonsFor(PluginId));
        Assert.Null(button.State.Label);
        Assert.True(button.State.Visible);
    }

    [Fact]
    public async Task InvokeAsync_RoutesToTheHandler()
    {
        await Service(Button("a")).InvokeAsync(PluginId, "a");

        await _handler.Received(1).InvokeButtonAsync("a", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_UnknownPlugin_IsANoOp()
    {
        await Service(Button("a")).InvokeAsync("no-such-plugin", "a");

        await _handler.DidNotReceive().InvokeButtonAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
