using System.Reflection;
using KHost.Abstractions.Services;
using KHost.UserInterface.Components.Dialogs;
using NSubstitute;

namespace KHost.UnitTests.UserInterface.Components.Dialogs;

public class ScreensDialogCastSearchTests
{
    private readonly ICastService _cast = Substitute.For<ICastService>();
    private readonly ScreensDialog _dialog = new();

    public ScreensDialogCastSearchTests()
        => typeof(ScreensDialog)
            .GetProperty("Cast", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(_dialog, _cast);

    /// <summary>
    /// The component is never rendered here, so the redraw it asks for at the end throws. That is a
    /// lifecycle artifact of testing the handler directly — the service call it is about has
    /// already happened. Only that one exception is swallowed.
    /// </summary>
    private async Task ToggleAsync()
    {
        try { await _dialog.ToggleCastSearchAsync(); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("render handle")) { }
    }

    [Fact]
    public async Task ToggleCastSearch_StartsBrowsing_WhenItIsOff()
    {
        _cast.IsDiscovering.Returns(false);

        await ToggleAsync();

        await _cast.Received(1).StartDiscoveryAsync(Arg.Any<CancellationToken>());
        await _cast.DidNotReceive().StopDiscoveryAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ToggleCastSearch_StopsBrowsing_WhenItIsOn()
    {
        _cast.IsDiscovering.Returns(true);

        await ToggleAsync();

        await _cast.Received(1).StopDiscoveryAsync(Arg.Any<CancellationToken>());
        await _cast.DidNotReceive().StartDiscoveryAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Disposing_StopsBrowsing_SoItDoesNotSweepAllNight()
    {
        _cast.IsDiscovering.Returns(true);

        _dialog.Dispose();

        // Nothing outside this dialog shows that the network is being swept.
        _cast.Received(1).StopDiscoveryAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Disposing_DoesNotTouchTheCastService_WhenItWasNeverSearching()
    {
        _cast.IsDiscovering.Returns(false);

        _dialog.Dispose();

        _cast.DidNotReceive().StopDiscoveryAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ToggleCastSearch_SurfacesAFailureInsteadOfThrowing()
    {
        _cast.IsDiscovering.Returns(false);
        _cast.StartDiscoveryAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("no mDNS")));

        await ToggleAsync();

        // A failed browse must not take the dialog down with it.
        Assert.False(_dialog.IsSearchingForCast);
    }
}
