using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain;
using KHost.Domain.Services;
using KHost.Domain.Services.QrCodes;
using KHost.Domain.Services.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.Domain.Services.QrCodes;

public class QrCodeServiceTests
{
    private readonly IVenuesService _venues = Substitute.For<IVenuesService>();
    private readonly IPlaybackService _playback = Substitute.For<IPlaybackService>();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);

    // Through a container, because the service looks playback up rather than taking it: a plugin
    // that shows a code and gates playback would otherwise close a constructor ring through it.
    private QrCodeService Service() => new(
        NullLogger<QrCodeService>.Instance, _venues,
        new ServiceCollection().AddSingleton(_playback).BuildServiceProvider(), _broker);

    /// <summary>The QR registrations as <c>AddDomain()</c> writes them, over this fixture's venue,
    /// playback and broker; the rest of the domain needs configuration and a database.</summary>
    private ServiceProvider HostContainer()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        foreach (var descriptor in new ServiceCollection().AddDomain()
                     .Where(d => d.ServiceType == typeof(IQrCodeService) || d.ServiceType == typeof(IQrCodeOfferService)))
        {
            ((IList<ServiceDescriptor>)services).Add(descriptor);
        }

        return services
            .AddSingleton(_venues)
            .AddSingleton(_playback)
            .AddSingleton<IMessageBroker>(_broker)
            .BuildServiceProvider();
    }

    private static QrCodeRegistration Code(string owner, string? caption = null) => new()
    {
        OwnerId = owner,
        Payload = $"https://example.test/{owner}",
        Caption = caption ?? owner,
    };

    /// <summary>A venue that has chosen "example", since none is the default.</summary>
    private void Arrange(Venue.VenueSettings? settings = null, string? source = "example")
    {
        settings ??= new();
        settings.QrCodeSource ??= source;

        _venues.ReadSelectedVenueAsync().Returns(new Venue { Name = "The Bar", Settings = settings });
    }

    /// <summary>The public read side is the registry itself, through the host's own registration:
    /// a second instance would read an empty registry and offer nothing, forever.</summary>
    [Fact]
    public async Task TheOfferReadSide_FromTheHostsRegistration_ReadsTheSameOfferTheRegistryBuilds()
    {
        Arrange(new Venue.VenueSettings { QrCodeCorner = OverlayCorner.TopLeft, QrCodeSafeZone = 2 });
        using var container = HostContainer();
        var registry = container.GetRequiredService<IQrCodeService>();
        var reader = container.GetRequiredService<IQrCodeOfferService>();

        await registry.RegisterAsync(Code("example", "Scan me"));

        var offered = await reader.ReadOfferAsync();
        Assert.NotNull(offered);
        Assert.Equal(await registry.ReadOfferAsync(), offered);
        Assert.Equal(("https://example.test/example", "Scan me", OverlayCorner.TopLeft, 2),
            (offered.Payload, offered.Caption, offered.Corner, offered.SafeZone));
    }

    /// <summary>A plugin reaches the read side; registering takes an owner id, so it must not.</summary>
    [Fact]
    public void TheOfferReadSide_OffersNoWayToRegister()
    {
        var members = typeof(IQrCodeOfferService).GetMethods()
            .Concat(typeof(IQrCodeOfferService).GetInterfaces().SelectMany(inherited => inherited.GetMethods()))
            .Select(method => method.Name);

        Assert.Equal([nameof(IQrCodeOfferService.ReadOfferAsync)], members);
    }

    /// <summary>Taking IPlaybackService directly closes a constructor ring through a plugin.</summary>
    [Fact]
    public void TheService_DoesNotTakePlaybackInItsConstructor()
    {
        var taken = typeof(QrCodeService)
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType);

        Assert.DoesNotContain(typeof(IPlaybackService), taken);
    }

    [Fact]
    public async Task ReadOfferAsync_NothingRegistered_OffersNothing()
    {
        Arrange();

        Assert.Null(await Service().ReadOfferAsync());
    }

    /// <summary>The offer carries what the owner registered, untouched.</summary>
    [Fact]
    public async Task ReadOfferAsync_OffersThePayloadAndCaptionRegistered()
    {
        Arrange();
        var service = Service();

        await service.RegisterAsync(Code("example", "Scan me"));

        var offer = Assert.IsType<QrCodeOffer>(await service.ReadOfferAsync());
        Assert.Equal("https://example.test/example", offer.Payload);
        Assert.Equal("Scan me", offer.Caption);
    }

    /// <summary>What fills an unchosen placement is the display's call, so the offer leaves it unset.</summary>
    [Fact]
    public async Task ReadOfferAsync_VenueNeverChoseACorner_LeavesCornerAndSizeUnset()
    {
        Arrange();
        var service = Service();

        await service.RegisterAsync(Code("example"));

        var offer = Assert.IsType<QrCodeOffer>(await service.ReadOfferAsync());
        Assert.Null(offer.Corner);
        Assert.Null(offer.Size);
    }

    /// <summary>It is the venue's screen, so its choice stands over the fallback.</summary>
    [Fact]
    public async Task ReadOfferAsync_VenueChoseACorner_OffersIt()
    {
        Arrange(new Venue.VenueSettings { QrCodeCorner = OverlayCorner.TopLeft, QrCodeSize = QrCodeSize.Large });
        var service = Service();

        await service.RegisterAsync(Code("example"));

        var offer = Assert.IsType<QrCodeOffer>(await service.ReadOfferAsync());
        Assert.Equal(OverlayCorner.TopLeft, offer.Corner);
        Assert.Equal(QrCodeSize.Large, offer.Size);
    }

    /// <summary>A never-asked venue stores zero, meaning "no preference" here, not "none".</summary>
    [Fact]
    public async Task ReadOfferAsync_VenueNeverAsked_LeavesSafeZoneAndOffsetUnset()
    {
        Arrange(new Venue.VenueSettings());
        var service = Service();

        await service.RegisterAsync(Code("example"));

        var offer = Assert.IsType<QrCodeOffer>(await service.ReadOfferAsync());
        Assert.Null(offer.SafeZone);
        Assert.Null(offer.Offset);
    }

    [Fact]
    public async Task ReadOfferAsync_VenueChoseASafeZoneAndOffset_OffersThem()
    {
        Arrange(new Venue.VenueSettings { QrCodeSafeZone = 4, QrCodeOffset = 3.5 });
        var service = Service();

        await service.RegisterAsync(Code("example"));

        var offer = Assert.IsType<QrCodeOffer>(await service.ReadOfferAsync());
        Assert.Equal(4, offer.SafeZone);
        Assert.Equal(3.5, offer.Offset);
    }

    /// <summary>Only the chosen source is offered; the rest stay registered, so switching is instant.</summary>
    [Fact]
    public async Task ReadOfferAsync_ASourceTheVenueDidNotChoose_IsHeldAndNotOffered()
    {
        Arrange();
        var service = Service();

        await service.RegisterAsync(Code("example"));
        await service.RegisterAsync(Code("online"));

        Assert.Equal("example", Assert.IsType<QrCodeOffer>(await service.ReadOfferAsync()).Caption);
    }

    /// <summary>The other one was already registered, so the switch needs nothing from its plugin.</summary>
    [Fact]
    public async Task ReadOfferAsync_VenueSwitchesSource_OffersTheOneAlreadyRegistered()
    {
        Arrange();
        var service = Service();
        await service.RegisterAsync(Code("example"));
        await service.RegisterAsync(Code("online"));

        Arrange(source: "online");

        Assert.Equal("online", Assert.IsType<QrCodeOffer>(await service.ReadOfferAsync()).Caption);
    }

    /// <summary>A chosen source with nothing to give leaves the choice standing, nothing offered.</summary>
    [Fact]
    public async Task ReadOfferAsync_ChosenSourceRegisteredNothing_OffersNothing()
    {
        Arrange();
        var service = Service();

        await service.RegisterAsync(Code("online"));

        Assert.Null(await service.ReadOfferAsync());
    }

    /// <summary>Showing twice is how a caller changes its own code, not how it gets a second one.</summary>
    [Fact]
    public async Task RegisterAsync_SameOwnerTwice_ReplacesRatherThanStacks()
    {
        Arrange();
        var service = Service();

        await service.RegisterAsync(Code("example", "Old"));
        await service.RegisterAsync(Code("example", "New"));

        Assert.Equal("New", Assert.IsType<QrCodeOffer>(await service.ReadOfferAsync()).Caption);
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

        Assert.Equal("example", Assert.IsType<QrCodeOffer>(await service.ReadOfferAsync()).Caption);

        await service.UnregisterAsync("example");

        Assert.Null(await service.ReadOfferAsync());
    }

    /// <summary>Hiding on the way out is right even when nothing was shown, so it must not throw.</summary>
    [Fact]
    public async Task UnregisterAsync_OwnerThatShowedNothing_IsNotAnError()
    {
        Arrange();
        var service = Service();

        await service.UnregisterAsync("never-showed-anything");

        Assert.Null(await service.ReadOfferAsync());
    }

    /// <summary>A code goes up because a venue chose it, not a plugin arriving; none is default.</summary>
    [Fact]
    public async Task ReadOfferAsync_VenueChoseNoSource_OffersNothing()
    {
        Arrange(source: null);
        var service = Service();

        await service.RegisterAsync(Code("example"));

        Assert.Null(await service.ReadOfferAsync());
    }

    /// <summary>No venue is nobody to have chosen, so it is the same answer rather than a default.</summary>
    [Fact]
    public async Task ReadOfferAsync_NoVenueSelected_OffersNothing()
    {
        _venues.ReadSelectedVenueAsync().Returns((Venue?)null);
        var service = Service();

        await service.RegisterAsync(Code("example"));

        Assert.Null(await service.ReadOfferAsync());
    }

    /// <summary>The venue asked for a clean picture while someone is singing.</summary>
    [Fact]
    public async Task ReadOfferAsync_HidingDuringSongs_OffersNothingWhileOneIsPlaying()
    {
        Arrange(new Venue.VenueSettings { QrCodeHideDuringSong = true });
        _playback.CurrentPerformance.Returns(new Performance { SingerId = Guid.NewGuid(), MediaId = Guid.NewGuid() });
        var service = Service();

        await service.RegisterAsync(Code("example"));

        Assert.Null(await service.ReadOfferAsync());
    }

    /// <summary>And they come back between songs without the owner asking again.</summary>
    [Fact]
    public async Task ReadOfferAsync_HidingDuringSongs_OffersItAgainWhenNothingIsPlaying()
    {
        Arrange(new Venue.VenueSettings { QrCodeHideDuringSong = true });
        var service = Service();
        await service.RegisterAsync(Code("example"));

        Assert.NotNull(await service.ReadOfferAsync());
    }

    /// <summary>A venue that did not ask keeps its codes up through the song.</summary>
    [Fact]
    public async Task ReadOfferAsync_NotHidingDuringSongs_KeepsThemUpWhileOnePlays()
    {
        Arrange();
        _playback.CurrentPerformance.Returns(new Performance { SingerId = Guid.NewGuid(), MediaId = Guid.NewGuid() });
        var service = Service();

        await service.RegisterAsync(Code("example"));

        Assert.NotNull(await service.ReadOfferAsync());
    }

    /// <summary>The display redraws on this, so an offer that moved must say so.</summary>
    [Fact]
    public async Task RegisterAsync_AnnouncesThatTheCodesMoved()
    {
        Arrange();
        var service = Service();
        var raised = 0;
        using var subscription = _broker.Subscribe<QrCodeOfferChanged>(_ => raised++);

        await service.RegisterAsync(Code("example"));

        // Published, not announced: the count is settled by the time the call returns.
        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task UnregisterAsync_AnnouncesThatTheCodesMoved()
    {
        Arrange();
        var service = Service();
        await service.RegisterAsync(Code("example"));
        var raised = 0;
        using var subscription = _broker.Subscribe<QrCodeOfferChanged>(_ => raised++);

        await service.UnregisterAsync("example");

        Assert.Equal(1, raised);
    }

    /// <summary>Withdrawing what was never offered moved nothing, so nothing is redrawn.</summary>
    [Fact]
    public async Task UnregisterAsync_OwnerThatShowedNothing_AnnouncesNothing()
    {
        Arrange();
        var service = Service();
        var raised = 0;
        using var subscription = _broker.Subscribe<QrCodeOfferChanged>(_ => raised++);

        await service.UnregisterAsync("never-showed-anything");

        Assert.Equal(0, raised);
    }
}
