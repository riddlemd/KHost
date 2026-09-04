using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Abstractions.Services.IPC;
using KHost.Domain.Services;
using KHost.Domain.Services.Messaging;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.Domain.Services;

public class ScreenQrCodeServiceTests
{
    private readonly IScreenServer _screens = Substitute.For<IScreenServer>();
    private readonly IVenuesService _venues = Substitute.For<IVenuesService>();
    private readonly IPlaybackService _playback = Substitute.For<IPlaybackService>();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);

    private ScreenQrCodeService Service() => new(
        NullLogger<ScreenQrCodeService>.Instance, _screens, _venues, _playback, _broker);

    private static ScreenQrCode Code(
        string owner, ScreenCorner? corner = null, ScreenQrSize? size = null, string? caption = null) => new()
    {
        OwnerId = owner,
        ImageUrl = $"data:image/png;base64,{owner}",
        Corner = corner,
        Size = size,
        Caption = caption,
    };

    private void Arrange(Venue.VenueSettings? settings = null)
        => _venues.ReadSelectedVenueAsync().Returns(new Venue { Name = "The Bar", Settings = settings ?? new() });

    [Fact]
    public async Task BuildAsync_NothingShown_SendsNoCodes()
    {
        Arrange();

        Assert.Empty((await Service().BuildAsync()).Codes);
    }

    /// <summary>A venue that has never been asked still has to put a code somewhere sensible.</summary>
    [Fact]
    public async Task ShowAsync_NoCornerAnywhere_LandsBottomRightAtMedium()
    {
        Arrange();
        var service = Service();

        await service.ShowAsync(Code("example"));

        var placed = Assert.Single((await service.BuildAsync()).Codes);
        Assert.Equal(ScreenCorner.BottomRight, placed.Corner);
        Assert.Equal(ScreenQrSize.Medium, placed.Size);
    }

    /// <summary>It is the venue's screen, so its choice stands over the fallback.</summary>
    [Fact]
    public async Task ShowAsync_VenueChoseACorner_UsesIt()
    {
        Arrange(new Venue.VenueSettings { QrCodeCorner = ScreenCorner.TopLeft, QrCodeSize = ScreenQrSize.Large });
        var service = Service();

        await service.ShowAsync(Code("example"));

        var placed = Assert.Single((await service.BuildAsync()).Codes);
        Assert.Equal(ScreenCorner.TopLeft, placed.Corner);
        Assert.Equal(ScreenQrSize.Large, placed.Size);
    }

    /// <summary>An owner that names one knows something the venue does not — a code beside its own overlay.</summary>
    [Fact]
    public async Task ShowAsync_OwnerNamedACorner_OverridesTheVenue()
    {
        Arrange(new Venue.VenueSettings { QrCodeCorner = ScreenCorner.TopLeft });
        var service = Service();

        await service.ShowAsync(Code("example", corner: ScreenCorner.BottomLeft, size: ScreenQrSize.Small));

        var placed = Assert.Single((await service.BuildAsync()).Codes);
        Assert.Equal(ScreenCorner.BottomLeft, placed.Corner);
        Assert.Equal(ScreenQrSize.Small, placed.Size);
    }

    /// <summary>Two owners is the case this is keyed for — the online service beside a plugin.</summary>
    [Fact]
    public async Task ShowAsync_TwoOwnersInDifferentCorners_BothAreShown()
    {
        Arrange();
        var service = Service();

        await service.ShowAsync(Code("example", ScreenCorner.BottomRight));
        await service.ShowAsync(Code("online", ScreenCorner.TopLeft));

        var codes = (await service.BuildAsync()).Codes;

        Assert.Equal(2, codes.Count);
        Assert.Contains(codes, code => code.ImageUrl.EndsWith("example"));
        Assert.Contains(codes, code => code.ImageUrl.EndsWith("online"));
    }

    /// <summary>
    /// A corner holds one code. Two drawn on top of each other is worse than the older one going,
    /// and the newest claim is the one someone just asked for.
    /// </summary>
    [Fact]
    public async Task ShowAsync_TwoOwnersInOneCorner_TheNewestClaimWins()
    {
        Arrange();
        var service = Service();

        await service.ShowAsync(Code("example", ScreenCorner.TopRight));
        await service.ShowAsync(Code("online", ScreenCorner.TopRight));

        var placed = Assert.Single((await service.BuildAsync()).Codes);
        Assert.EndsWith("online", placed.ImageUrl);
    }

    /// <summary>Showing twice is how a caller changes its own code, not how it gets a second one.</summary>
    [Fact]
    public async Task ShowAsync_SameOwnerTwice_ReplacesRatherThanStacks()
    {
        Arrange();
        var service = Service();

        await service.ShowAsync(Code("example", ScreenCorner.TopRight, caption: "Old"));
        await service.ShowAsync(Code("example", ScreenCorner.TopRight, caption: "New"));

        Assert.Equal("New", Assert.Single((await service.BuildAsync()).Codes).Caption);
    }

    [Fact]
    public async Task HideAsync_TakesDownOnlyThatOwnersCode()
    {
        Arrange();
        var service = Service();
        await service.ShowAsync(Code("example", ScreenCorner.BottomRight));
        await service.ShowAsync(Code("online", ScreenCorner.TopLeft));

        await service.HideAsync("example");

        var placed = Assert.Single((await service.BuildAsync()).Codes);
        Assert.EndsWith("online", placed.ImageUrl);
    }

    /// <summary>Hiding on the way out is right even when nothing was shown, so it must not throw.</summary>
    [Fact]
    public async Task HideAsync_OwnerThatShowedNothing_IsNotAnError()
    {
        Arrange();
        var service = Service();

        await service.HideAsync("never-showed-anything");

        Assert.Empty((await service.BuildAsync()).Codes);
    }

    /// <summary>The venue asked for a clean picture while someone is singing.</summary>
    [Fact]
    public async Task BuildAsync_HidingDuringSongs_SendsNoneWhileOneIsPlaying()
    {
        Arrange(new Venue.VenueSettings { QrCodeHideDuringSong = true });
        _playback.CurrentPerformance.Returns(new Performance { SingerId = Guid.NewGuid(), MediaId = Guid.NewGuid() });
        var service = Service();

        await service.ShowAsync(Code("example"));

        Assert.Empty((await service.BuildAsync()).Codes);
    }

    /// <summary>And they come back between songs without the owner asking again.</summary>
    [Fact]
    public async Task BuildAsync_HidingDuringSongs_ShowsThemAgainWhenNothingIsPlaying()
    {
        Arrange(new Venue.VenueSettings { QrCodeHideDuringSong = true });
        var service = Service();
        await service.ShowAsync(Code("example"));

        Assert.Single((await service.BuildAsync()).Codes);
    }

    /// <summary>A venue that did not ask keeps its codes up through the song.</summary>
    [Fact]
    public async Task BuildAsync_NotHidingDuringSongs_KeepsThemUpWhileOnePlays()
    {
        Arrange();
        _playback.CurrentPerformance.Returns(new Performance { SingerId = Guid.NewGuid(), MediaId = Guid.NewGuid() });
        var service = Service();

        await service.ShowAsync(Code("example"));

        Assert.Single((await service.BuildAsync()).Codes);
    }

    [Fact]
    public async Task ShowAsync_SendsTheWholeSetToTheScreens()
    {
        Arrange();
        using var service = Service();

        await service.ShowAsync(Code("example"));

        await _screens.Received(1).BroadcastCommandAsync(Arg.Is<SetScreenQrCodesCommand>(
            command => command.Codes.Count == 1));
    }

    /// <summary>
    /// A venue moving its codes has to reach the screens on its own: nobody calls ShowAsync again
    /// for a setting that changed under them.
    /// </summary>
    [Fact]
    public async Task SelectedVenueChanged_RepublishesWhereTheCodesSit()
    {
        Arrange();
        using var service = Service();
        await service.ShowAsync(Code("example"));
        _screens.ClearReceivedCalls();

        _venues.ReadSelectedVenueAsync().Returns(new Venue
        {
            Name = "The Bar",
            Settings = new Venue.VenueSettings { QrCodeCorner = ScreenCorner.TopLeft },
        });

        _broker.Announce(new SelectedVenueChanged());

        await WaitForBroadcastAsync(command => command.Codes.Single().Corner == ScreenCorner.TopLeft);
    }

    /// <summary>The whole point of holding this host-side: a screen that drops mid-show comes back correct.</summary>
    [Fact]
    public async Task ScreenConnected_SendsTheCodesToThatScreen()
    {
        Arrange();
        using var service = Service();
        await service.ShowAsync(Code("example"));

        _screens.ScreenConnected += Raise.EventWith(new ScreenConnectionEventArgs { Connection = Connection("screen-1") });

        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (_screens.ReceivedCalls().Any(call =>
                    call.GetMethodInfo().Name == nameof(IScreenServer.SendCommandAsync)))
                return;

            await Task.Delay(10);
        }

        Assert.Fail("The codes were never sent to the reconnecting screen.");
    }

    private static IScreenConnection Connection(string screenId)
    {
        var connection = Substitute.For<IScreenConnection>();
        connection.ScreenId.Returns(screenId);
        return connection;
    }

    // The handlers hand off to Task.Run so the hub thread is never held, so an assertion made
    // straight after an announce races the publish rather than observing it.
    private async Task WaitForBroadcastAsync(Func<SetScreenQrCodesCommand, bool> matches)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (_screens.ReceivedCalls().Any(call =>
                    call.GetMethodInfo().Name == nameof(IScreenServer.BroadcastCommandAsync)
                    && call.GetArguments().FirstOrDefault() is SetScreenQrCodesCommand command
                    && matches(command)))
                return;

            await Task.Delay(10);
        }

        Assert.Fail("The codes were never broadcast.");
    }
}
