using Bunit;
using KHost.UserInterface.Components;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;

namespace KHost.UnitTests.UserInterface.Components;

public class HelpButtonTests : BunitContext
{
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();

    public HelpButtonTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        Services.AddSingleton(_dialogs);
    }

    [Fact]
    public void Renders_WithAnAccessibleLabel()
    {
        var button = Render<HelpButton>();

        Assert.Equal("Help and shortcuts", button.Find("button").GetAttribute("aria-label"));
    }

    [Fact]
    public void Click_OpensTheHelpDialog()
    {
        var button = Render<HelpButton>();

        button.Find("button").Click();

        _dialogs.Received(1).ShowHelpAsync(Arg.Any<Action?>());
    }
}
