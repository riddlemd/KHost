using KHost.Abstractions.Services;
using KHost.UserInterface.Components.Dialogs;
using KHost.UserInterface.Models;
using KHost.UserInterface.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.UserInterface.Services;

/// <summary>Confirming "Launch Screen" goes the Display menu's way, so the provider's one-screen
/// rule covers it too.</summary>
public class DialogServiceTests
{
    private readonly IDisplayProvider _screens = Display("Local Display", searches: false, connectedId: null);
    private readonly IDisplayProvider _cast = Display("Chromecast", searches: true, connectedId: null);

    private static IDisplayProvider Display(string name, bool searches, string? connectedId)
    {
        var display = Substitute.For<IDisplayProvider>();
        display.Name.Returns(name);
        display.SearchesForDevices.Returns(searches);
        display.ConnectedDeviceId.Returns(connectedId);
        display.Devices.Returns([new DisplayDevice { Id = name + "-device", Name = name }]);

        return display;
    }

    private static async Task ConfirmNoScreensAsync(DialogService service)
    {
        BaseDialogRequest? shown = null;
        service.ShowRequested += (_, request) => shown = request;

        await service.ShowNoScreensAsync();

        await Assert.IsType<ConfirmationDialog.DialogRequest>(shown).OnConfirm();
    }

    [Fact]
    public async Task ShowNoScreensAsync_Confirmed_ConnectsTheLocalDisplay()
    {
        var service = new DialogService(NullLogger<DialogService>.Instance, [_cast, _screens]);

        await ConfirmNoScreensAsync(service);

        await _screens.Received(1).ConnectAsync("Local Display-device", Arg.Any<CancellationToken>());
        await _cast.DidNotReceive().ConnectAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ShowNoScreensAsync_Confirmed_TakesTheSongOffAnyOtherDisplayFirst()
    {
        _cast.ConnectedDeviceId.Returns("tv");
        var service = new DialogService(NullLogger<DialogService>.Instance, [_cast, _screens]);

        await ConfirmNoScreensAsync(service);

        Received.InOrder(() =>
        {
            _cast.DisconnectAsync(Arg.Any<CancellationToken>());
            _screens.ConnectAsync("Local Display-device", Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task ShowNoScreensAsync_Confirmed_LeavesAnIdleDisplayAlone()
    {
        var service = new DialogService(NullLogger<DialogService>.Instance, [_cast, _screens]);

        await ConfirmNoScreensAsync(service);

        await _cast.DidNotReceive().DisconnectAsync(Arg.Any<CancellationToken>());
    }
}
