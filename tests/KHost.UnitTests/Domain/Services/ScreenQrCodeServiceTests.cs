using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Abstractions.Services.IPC;
using KHost.Domain.Services;
using KHost.Domain.Services.Screens;
using KHost.Domain.Services.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.Domain.Services;

public class ScreenQrCodeServiceTests
{
    private readonly IScreenServer _screens = Substitute.For<IScreenServer>();
    private readonly IVenuesService _venues = Substitute.For<IVenuesService>();
    private readonly IPlaybackService _playback = Substitute.For<IPlaybackService>();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);

    // Through a container, because the service looks playback up rather than taking it: a plugin
    // that shows a code and gates playback would otherwise close a constructor ring through it.
    private ScreenQrCodeService Service() => new(
        NullLogger<ScreenQrCodeService>.Instance, _screens, _venues,
        new ServiceCollection().AddSingleton(_playback).BuildServiceProvider(), _broker);

    private static ScreenQrCode Code(string owner, string? caption = null) => new()
    {
        OwnerId = owner,
        Payload = $"https://example.test/{owner}",
        // The placement carries a picture the host drew, not the string it came from, so the
        // caption is what a test reads to tell whose code landed.
        Caption = caption ?? owner,
    };

    /// <summary>
    /// A venue that has chosen "example", since none is the default and none shows nothing. Pass
    /// <paramref name="source"/> explicitly for the tests about choosing itself.
    /// </summary>
    private void Arrange(Venue.VenueSettings? settings = null, string? source = "example")
    {
        settings ??= new();
        settings.QrCodeSource ??= source;

        _venues.ReadSelectedVenueAsync().Returns(new Venue { Name = "The Bar", Settings = settings });
    }

    /// <summary>
    /// Taking IPlaybackService here hung the app before it logged a line, and nothing in the
    /// suite noticed: a plugin is registered once and pointed at every extension interface it
    /// implements, so a plugin that shows a code and gates playback closes a ring — the plugin
    /// needs this service, this service needs playback, playback needs every IMediaPlaybackGate,
    /// and one of those is the plugin still being constructed. Asserted against the constructor
    /// rather than by building that graph, because the failure is an infinite recursion: a test
    /// that reproduced it would hang the suite instead of failing it.
    /// </summary>
    [Fact]
    public void TheService_DoesNotTakePlaybackInItsConstructor()
    {
        var taken = typeof(ScreenQrCodeService)
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType);

        Assert.DoesNotContain(typeof(IPlaybackService), taken);
    }

    [Fact]
    public async Task BuildAsync_NothingShown_SendsNoCodes()
    {
        Arrange();

        Assert.Empty((await Service().BuildAsync()).Codes);
    }

    /// <summary>A venue that has never been asked still has to put a code somewhere sensible.</summary>
    [Fact]
    public async Task RegisterAsync_NoCornerAnywhere_LandsBottomRightAtMedium()
    {
        Arrange();
        var service = Service();

        await service.RegisterAsync(Code("example"));

        var placed = Assert.Single((await service.BuildAsync()).Codes);
        Assert.Equal(ScreenCorner.BottomRight, placed.Corner);
        Assert.Equal(ScreenQrSize.Medium, placed.Size);
    }

    /// <summary>It is the venue's screen, so its choice stands over the fallback.</summary>
    [Fact]
    public async Task RegisterAsync_VenueChoseACorner_UsesIt()
    {
        Arrange(new Venue.VenueSettings { QrCodeCorner = ScreenCorner.TopLeft, QrCodeSize = ScreenQrSize.Large });
        var service = Service();

        await service.RegisterAsync(Code("example"));

        var placed = Assert.Single((await service.BuildAsync()).Codes);
        Assert.Equal(ScreenCorner.TopLeft, placed.Corner);
        Assert.Equal(ScreenQrSize.Large, placed.Size);
    }

    /// <summary>
    /// Zero is "no preference", not "none". A venue that has never been asked stores it, and the
    /// stored default of a value type is zero whatever the property initializer says — so the
    /// screen must never be handed one, or a code arrives with no quiet zone and nothing to scan.
    /// </summary>
    [Fact]
    public async Task RegisterAsync_VenueNeverAsked_TakesTheHostsOwnSafeZoneAndOffset()
    {
        Arrange(new Venue.VenueSettings());
        var service = Service();

        await service.RegisterAsync(Code("example"));

        var placed = Assert.Single((await service.BuildAsync()).Codes);
        Assert.Equal(1, placed.SafeZone);
        Assert.Equal(0.2, placed.Offset);
    }

    [Fact]
    public async Task RegisterAsync_VenueChoseASafeZoneAndOffset_UsesThem()
    {
        Arrange(new Venue.VenueSettings { QrCodeSafeZone = 4, QrCodeOffset = 3.5 });
        var service = Service();

        await service.RegisterAsync(Code("example"));

        var placed = Assert.Single((await service.BuildAsync()).Codes);
        Assert.Equal(4, placed.SafeZone);
        Assert.Equal(3.5, placed.Offset);
    }

    /// <summary>
    /// The picture carries no quiet zone of its own any more, so the module count must be what is
    /// actually drawn. Counting the undrawn border would have the screen size every code as though
    /// it were eight modules wider, and the per-module scannability floor would measure nothing.
    /// </summary>
    [Fact]
    public async Task RegisterAsync_ModuleCount_IsWhatTheImageDraws()
    {
        Arrange(new Venue.VenueSettings());
        var service = Service();

        await service.RegisterAsync(Code("example"));

        var placed = Assert.Single((await service.BuildAsync()).Codes);
        var svg = System.Text.Encoding.UTF8.GetString(
            Convert.FromBase64String(placed.ImageUrl["data:image/svg+xml;base64,".Length..]));

        // The SVG is drawn one unit per module, so its declared size is the module count.
        Assert.Contains($"width=\"{placed.Modules}\"", svg);
        Assert.Contains($"height=\"{placed.Modules}\"", svg);
    }

    /// <summary>An owner that names one knows something the venue does not — a code beside its own overlay.</summary>
    /// <summary>
    /// Only the chosen source is drawn. The others keep registering against a venue that may pick
    /// them later, and nothing tells them they were passed over — which is what makes switching
    /// source mid-show immediate.
    /// </summary>
    [Fact]
    public async Task BuildAsync_ASourceTheVenueDidNotChoose_IsHeldAndNotDrawn()
    {
        Arrange();
        var service = Service();

        await service.RegisterAsync(Code("example"));
        await service.RegisterAsync(Code("online"));

        Assert.Equal("example", Assert.Single((await service.BuildAsync()).Codes).Caption);
    }

    /// <summary>The other one was already registered, so the switch needs nothing from its plugin.</summary>
    [Fact]
    public async Task BuildAsync_VenueSwitchesSource_DrawsTheOneAlreadyRegistered()
    {
        Arrange();
        var service = Service();
        await service.RegisterAsync(Code("example"));
        await service.RegisterAsync(Code("online"));

        Arrange(source: "online");

        Assert.Equal("online", Assert.Single((await service.BuildAsync()).Codes).Caption);
    }

    /// <summary>
    /// A chosen source that has nothing to give — a plugin not signed in yet, or one that
    /// withdrew. The venue's choice stands; there is simply nothing to draw against it.
    /// </summary>
    [Fact]
    public async Task BuildAsync_ChosenSourceRegisteredNothing_SendsNone()
    {
        Arrange();
        var service = Service();

        await service.RegisterAsync(Code("online"));

        Assert.Empty((await service.BuildAsync()).Codes);
    }

    /// <summary>Showing twice is how a caller changes its own code, not how it gets a second one.</summary>
    [Fact]
    public async Task RegisterAsync_SameOwnerTwice_ReplacesRatherThanStacks()
    {
        Arrange();
        var service = Service();

        await service.RegisterAsync(Code("example", "Old"));
        await service.RegisterAsync(Code("example", "New"));

        Assert.Equal("New", Assert.Single((await service.BuildAsync()).Codes).Caption);
    }

    [Fact]
    public async Task UnregisterAsync_TakesDownOnlyThatOwnersCode()
    {
        Arrange();
        var service = Service();
        await service.RegisterAsync(Code("example"));
        await service.RegisterAsync(Code("online"));

        // The one the venue is not showing. What is on screen must not move.
        await service.UnregisterAsync("online");

        Assert.Equal("example", Assert.Single((await service.BuildAsync()).Codes).Caption);

        await service.UnregisterAsync("example");

        Assert.Empty((await service.BuildAsync()).Codes);
    }

    /// <summary>Hiding on the way out is right even when nothing was shown, so it must not throw.</summary>
    [Fact]
    public async Task UnregisterAsync_OwnerThatShowedNothing_IsNotAnError()
    {
        Arrange();
        var service = Service();

        await service.UnregisterAsync("never-showed-anything");

        Assert.Empty((await service.BuildAsync()).Codes);
    }

    /// <summary>
    /// None is the default, and the answer whatever a plugin registers. A code invites a room to
    /// scan it, so it goes up because a venue chose it and not because a plugin arrived.
    /// </summary>
    [Fact]
    public async Task BuildAsync_VenueChoseNoSource_SendsNone()
    {
        Arrange(source: null);
        var service = Service();

        await service.RegisterAsync(Code("example"));

        Assert.Empty((await service.BuildAsync()).Codes);
    }

    /// <summary>No venue is nobody to have chosen, so it is the same answer rather than a default.</summary>
    [Fact]
    public async Task BuildAsync_NoVenueSelected_SendsNone()
    {
        _venues.ReadSelectedVenueAsync().Returns((Venue?)null);
        var service = Service();

        await service.RegisterAsync(Code("example"));

        Assert.Empty((await service.BuildAsync()).Codes);
    }

    /// <summary>The venue asked for a clean picture while someone is singing.</summary>
    [Fact]
    public async Task BuildAsync_HidingDuringSongs_SendsNoneWhileOneIsPlaying()
    {
        Arrange(new Venue.VenueSettings { QrCodeHideDuringSong = true });
        _playback.CurrentPerformance.Returns(new Performance { SingerId = Guid.NewGuid(), MediaId = Guid.NewGuid() });
        var service = Service();

        await service.RegisterAsync(Code("example"));

        Assert.Empty((await service.BuildAsync()).Codes);
    }

    /// <summary>And they come back between songs without the owner asking again.</summary>
    [Fact]
    public async Task BuildAsync_HidingDuringSongs_ShowsThemAgainWhenNothingIsPlaying()
    {
        Arrange(new Venue.VenueSettings { QrCodeHideDuringSong = true });
        var service = Service();
        await service.RegisterAsync(Code("example"));

        Assert.Single((await service.BuildAsync()).Codes);
    }

    /// <summary>A venue that did not ask keeps its codes up through the song.</summary>
    [Fact]
    public async Task BuildAsync_NotHidingDuringSongs_KeepsThemUpWhileOnePlays()
    {
        Arrange();
        _playback.CurrentPerformance.Returns(new Performance { SingerId = Guid.NewGuid(), MediaId = Guid.NewGuid() });
        var service = Service();

        await service.RegisterAsync(Code("example"));

        Assert.Single((await service.BuildAsync()).Codes);
    }

    /// <summary>
    /// The caller hands over what the code should say and the host draws it. A provider that
    /// renders its own (Example returns an SVG) passes the string instead: two codes carrying the
    /// same text scan to the same place whatever they look like.
    /// </summary>
    [Fact]
    public async Task RegisterAsync_DrawsThePayloadAsAVector()
    {
        Arrange();
        var service = Service();

        await service.RegisterAsync(Code("example"));

        var placed = Assert.Single((await service.BuildAsync()).Codes);

        // SVG, not pixels: the code sits in a corner a few centimetres across, where the module
        // edges are the whole of whether a phone can read it.
        Assert.StartsWith("data:image/svg+xml;base64,", placed.ImageUrl);
        Assert.Contains("<svg", Decode(placed.ImageUrl), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The screen sizes off the module count, so a code that arrives without one has nothing to
    /// hold it above the size a long payload makes unreadable.
    /// </summary>
    [Fact]
    public async Task RegisterAsync_SaysHowManyModulesTheCodeIsAcross()
    {
        Arrange();
        var service = Service();

        await service.RegisterAsync(Code("example"));

        var placed = Assert.Single((await service.BuildAsync()).Codes);

        // 21 modules is the smallest a QR can be, before its four-module quiet zone.
        Assert.True(placed.Modules >= 21, $"Expected a real module count, got {placed.Modules}");
    }

    /// <summary>A longer payload needs more modules — the reason the count is sent at all.</summary>
    [Fact]
    public async Task RegisterAsync_ALongerPayload_NeedsMoreModules()
    {
        Arrange();
        var service = Service();

        await service.RegisterAsync(new ScreenQrCode { OwnerId = "example", Payload = "https://k.test/a" });
        var shortCode = Assert.Single((await service.BuildAsync()).Codes);

        await service.RegisterAsync(new ScreenQrCode
        {
            OwnerId = "example",
            Payload = "https://app.example.com/remote/join?channel=" + new string('x', 180),
        });
        var longCode = Assert.Single((await service.BuildAsync()).Codes);

        Assert.True(longCode.Modules > shortCode.Modules);
    }

    /// <summary>
    /// The same payload draws the same picture — the encoder caches, and a venue switching
    /// between two sources pointing at one URL must not redraw it.
    /// </summary>
    [Fact]
    public async Task RegisterAsync_TheSamePayloadTwice_DrawsTheSameCode()
    {
        Arrange();
        var service = Service();

        await service.RegisterAsync(new ScreenQrCode { OwnerId = "example", Payload = "https://k.test/same" });
        var first = Assert.Single((await service.BuildAsync()).Codes);

        await service.RegisterAsync(new ScreenQrCode { OwnerId = "example", Payload = "https://k.test/same" });
        var second = Assert.Single((await service.BuildAsync()).Codes);

        Assert.Equal(first.ImageUrl, second.ImageUrl);
    }

    private static string Decode(string dataUri)
        => System.Text.Encoding.UTF8.GetString(
            Convert.FromBase64String(dataUri["data:image/svg+xml;base64,".Length..]));

    [Fact]
    public async Task RegisterAsync_SendsTheWholeSetToTheScreens()
    {
        Arrange();
        using var service = Service();

        await service.RegisterAsync(Code("example"));

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
        await service.RegisterAsync(Code("example"));
        _screens.ClearReceivedCalls();

        _venues.ReadSelectedVenueAsync().Returns(new Venue
        {
            Name = "The Bar",
            Settings = new Venue.VenueSettings { QrCodeSource = "example", QrCodeCorner = ScreenCorner.TopLeft },
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
        await service.RegisterAsync(Code("example"));

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
