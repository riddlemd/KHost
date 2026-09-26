using Bunit;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services;
using KHost.Domain.Services.Messaging;
using KHost.UserInterface.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.UserInterface.Components;

/// <summary>The generic notice host mounted once in MainLayout: fixed over the content, several
/// stacked, auto-dismissing, each closable on its own.</summary>
public class FlashBannerTests : BunitContext
{
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);
    private readonly FlashService _flash;

    public FlashBannerTests()
    {
        _flash = new FlashService(_broker);

        Services.AddSingleton<IFlashService>(_flash);
        Services.AddSingleton<IMessageBroker>(_broker);
    }

    [Fact]
    public void ARaisedNotice_Shows()
    {
        var cut = Render<FlashBanner>();

        _flash.Show("App settings saved.");

        cut.WaitForAssertion(() =>
            Assert.Equal("App settings saved.", cut.Find(".kh-flash__text").TextContent));
    }

    [Fact]
    public void ASuccessNotice_IsAStatusRegion()
    {
        var cut = Render<FlashBanner>();

        _flash.Show("App settings saved.");

        cut.WaitForAssertion(() => Assert.Equal("status", cut.Find(".kh-flash").GetAttribute("role")));
    }

    /// <summary>A warning is announced immediately by a screen reader rather than politely, since it
    /// is the one type the host stays until closed or a while longer.</summary>
    [Fact]
    public void AWarningNotice_IsAnAlertRegion()
    {
        var cut = Render<FlashBanner>();

        _flash.Show("That name is already taken.", FlashType.Warning);

        cut.WaitForAssertion(() => Assert.Equal("alert", cut.Find(".kh-flash").GetAttribute("role")));
    }

    [Fact]
    public void SeveralNotices_Stack()
    {
        var cut = Render<FlashBanner>();

        _flash.Show("First.");
        _flash.Show("Second.");

        cut.WaitForAssertion(() => Assert.Equal(
            ["First.", "Second."],
            cut.FindAll(".kh-flash__text").Select(e => e.TextContent)));
    }

    [Fact]
    public void TheCloseButton_DismissesOnlyItsOwnNotice()
    {
        var cut = Render<FlashBanner>();
        _flash.Show("First.");
        _flash.Show("Second.");
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".kh-flash").Count));

        cut.FindAll(".kh-flash__dismiss")[0].Click();

        cut.WaitForAssertion(() => Assert.Equal(
            ["Second."],
            cut.FindAll(".kh-flash__text").Select(e => e.TextContent)));
    }

    /// <summary>The real 4-8s wait is swapped for the test seam, which resolves at once, so this
    /// does not have to sit idle for the real duration.</summary>
    [Fact]
    public void ANotice_AutoDismissesOnItsOwnTimer()
    {
        var cut = Render<FlashBanner>();
        cut.Instance.Delay = _ => Task.CompletedTask;

        _flash.Show("App settings saved.");

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".kh-flash")));
    }

    /// <summary>One message's timer completing must not take a message shown after it down too.</summary>
    [Fact]
    public void ANotice_AutoDismissing_LeavesALaterNoticeUpUntilItsOwnTimer()
    {
        var cut = Render<FlashBanner>();
        cut.Instance.Delay = _ => Task.CompletedTask;
        _flash.Show("First.");
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".kh-flash")));

        // A second timer that never resolves, so "Second." must stay up.
        cut.Instance.Delay = _ => new TaskCompletionSource().Task;
        _flash.Show("Second.");

        cut.WaitForAssertion(() => Assert.Equal(
            ["Second."],
            cut.FindAll(".kh-flash__text").Select(e => e.TextContent)));
    }
}
