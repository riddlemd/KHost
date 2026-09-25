using AngleSharp.Dom;
using Bunit;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services.Messaging;
using KHost.UserInterface.Components;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace KHost.UnitTests.UserInterface.Components;

/// <summary>The display selector, which lives in the menu beside Venue and Theme: one display is
/// live across every provider, and this is how a host moves it.</summary>
public class SettingsButtonDisplayTests : BunitContext
{
    private readonly IDisplayProvider _screens = Substitute.For<IDisplayProvider>();
    private readonly IDisplayProvider _cast = Substitute.For<IDisplayProvider>();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);

    private IDisplayProvider[] _providers;

    public SettingsButtonDisplayTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        // NSubstitute hands back string.Empty for an unstubbed string, not null, so "nothing is
        // connected" has to be said or every test starts with a display live.
        _screens.ConnectedDeviceId.Returns((string?)null);
        _cast.ConnectedDeviceId.Returns((string?)null);

        _screens.Name.Returns("Local Display");
        _cast.Name.Returns("Chromecast");

        // The screens are opened, not found; a receiver is swept for.
        _screens.SearchesForDevices.Returns(false);
        _cast.SearchesForDevices.Returns(true);

        _screens.Devices.Returns([Device("Screen 1", "Local Display", model: "This computer")]);
        _cast.Devices.Returns([]);

        _providers = [_screens, _cast];

        var venues = Substitute.For<IVenuesService>();
        venues.ReadAllAsync(Arg.Any<int>(), Arg.Any<int>()).Returns(new PaginatedResult<Venue>());

        var permissions = Substitute.For<IPermissionService>();
        permissions.IsAdminAsync().Returns(true);
        permissions.HasAsync(Arg.Any<KHostPermission>()).Returns(true);

        var appSettings = Substitute.For<IAppSettingsService>();
        appSettings.Current.Returns(new AppSettings());

        Services.AddSingleton(venues);
        Services.AddSingleton(permissions);
        Services.AddSingleton(appSettings);
        Services.AddSingleton(Substitute.For<IBreakMusicService>());
        Services.AddSingleton(Substitute.For<IDialogService>());
        Services.AddSingleton(Substitute.For<IThemeService>());
        Services.AddSingleton<IMessageBroker>(_broker);
        Services.AddSingleton<IEnumerable<IDisplayProvider>>(_ => _providers);
    }

    private static DisplayDevice Device(
        string id, string name, string? model = null, string? address = null)
        => new()
        {
            Id = id,
            Name = name,
            Model = model,
            Address = address,
        };

    private static void Connect(IDisplayProvider provider, string deviceId)
        => provider.ConnectedDeviceId.Returns(deviceId);

    /// <summary>Opens the menu, then the Display section, and hands back the flyout's rows.</summary>
    private (IRenderedComponent<SettingsButton> Menu, IReadOnlyList<IElement> Rows) OpenDisplaySection()
    {
        var menu = Render<SettingsButton>();
        menu.Find(".kh-dropdown__trigger").Click();

        var row = menu.FindAll(".kh-dropdown__item")
            .First(i => i.TextContent.Contains("Display", StringComparison.Ordinal));
        row.Click();

        return (menu, menu.FindAll(".kh-settings-menu__flyout .kh-dropdown__item"));
    }

    private static IElement Row(IReadOnlyList<IElement> rows, string text)
        => rows.FirstOrDefault(r => r.TextContent.Contains(text, StringComparison.Ordinal))
           ?? throw new InvalidOperationException($"No row reads '{text}'");

    // --- what the row in Quick Settings says ---

    [Fact]
    public void TheRow_ReadsNone_WithNothingConnected()
    {
        var menu = Render<SettingsButton>();
        menu.Find(".kh-dropdown__trigger").Click();

        Assert.Equal("None", menu.Instance.DisplayValue);
    }

    /// <summary>A host reads the room, not the wiring, so the device names the row.</summary>
    [Fact]
    public void TheRow_NamesTheDevice_NotTheTransport()
    {
        _cast.Devices.Returns([Device("tv-1", "Living Room TV")]);
        Connect(_cast, "tv-1");

        var menu = Render<SettingsButton>();
        menu.Find(".kh-dropdown__trigger").Click();

        Assert.Equal("Living Room TV", menu.Instance.DisplayValue);
    }

    /// <summary>A receiver can report a connection before it has listed the device.</summary>
    [Fact]
    public void TheRow_FallsBackToTheTransport_WhenTheDeviceIsNotListedYet()
    {
        Connect(_cast, "tv-1");

        var menu = Render<SettingsButton>();
        menu.Find(".kh-dropdown__trigger").Click();

        Assert.Equal("Chromecast", menu.Instance.DisplayValue);
    }

    // --- what the flyout offers ---

    [Fact]
    public void TheFlyout_ListsEveryDevice_AndTheSearch()
    {
        _cast.Devices.Returns([Device("tv-1", "Living Room TV", address: "192.168.1.14")]);

        var (_, rows) = OpenDisplaySection();
        var text = string.Join(" | ", rows.Select(r => r.TextContent));

        Assert.Contains("Local Display", text);
        Assert.Contains("Living Room TV", text);
        Assert.Contains("192.168.1.14", text);
        Assert.Contains("Search for devices", text);
    }

    /// <summary>Unlike a split button's primary half, the live device stays in the list: a checked
    /// row is the ordinary way to show the current choice, beside the ones you could switch to.</summary>
    [Fact]
    public void TheFlyout_ChecksTheLiveDevice_AndStillListsIt()
    {
        Connect(_screens, "Screen 1");

        var (_, rows) = OpenDisplaySection();
        var live = Row(rows, "Local Display");

        Assert.Contains("kh-dropdown__item--selected", live.ClassName);
    }

    [Fact]
    public void TheFlyout_OffersNoTurnOff_WithNothingConnected()
    {
        var (_, rows) = OpenDisplaySection();

        Assert.DoesNotContain(rows, r => r.TextContent.Contains("Turn off", StringComparison.Ordinal));
    }

    [Fact]
    public void TheFlyout_OffersNoSearch_WithOnlyTheScreens()
    {
        _providers = [_screens];

        var (_, rows) = OpenDisplaySection();

        Assert.DoesNotContain(rows, r => r.TextContent.Contains("Search", StringComparison.Ordinal));
    }

    /// <summary>One bad plugin must not empty the list and strand the host with no way to switch.</summary>
    [Fact]
    public void TheFlyout_SkipsAProviderThatThrows()
    {
        _cast.Devices.Returns(_ => throw new InvalidOperationException("mid-sweep"));

        var (_, rows) = OpenDisplaySection();

        Assert.Contains(rows, r => r.TextContent.Contains("Local Display", StringComparison.Ordinal));
    }

    // --- what pressing a row does ---

    /// <summary>Two displays carrying one song is the state this control exists to make unreachable.</summary>
    [Fact]
    public async Task ChoosingADevice_DisconnectsWhateverWasLive_BeforeConnecting()
    {
        _cast.Devices.Returns([Device("tv-1", "Living Room TV")]);
        Connect(_screens, "Screen 1");

        var (_, rows) = OpenDisplaySection();
        await Row(rows, "Living Room TV").ClickAsync(new());

        await _screens.Received(1).DisconnectAsync(Arg.Any<CancellationToken>());
        await _cast.Received(1).ConnectAsync("tv-1", Arg.Any<CancellationToken>());
    }

    /// <summary>Pressing the row that is already live should not tear the song down and rebuild it.</summary>
    [Fact]
    public async Task ChoosingTheLiveDevice_DoesNothingToTheSong()
    {
        Connect(_screens, "Screen 1");

        var (_, rows) = OpenDisplaySection();
        await Row(rows, "Local Display").ClickAsync(new());

        await _screens.DidNotReceive().DisconnectAsync(Arg.Any<CancellationToken>());
        await _screens.DidNotReceive().ConnectAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TurnOff_DisconnectsWhateverWasCarryingTheSong()
    {
        Connect(_cast, "tv-1");
        _cast.Devices.Returns([Device("tv-1", "Living Room TV")]);

        var (_, rows) = OpenDisplaySection();
        await Row(rows, "Turn off").ClickAsync(new());

        await _cast.Received(1).DisconnectAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>A transport that throws must not take the menu down with it.</summary>
    [Fact]
    public async Task ChoosingADevice_SurvivesAProviderThatThrows()
    {
        _cast.Devices.Returns([Device("tv-1", "Living Room TV")]);
        _cast.ConnectAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<bool>>(_ => throw new InvalidOperationException("receiver went away"));

        var (menu, rows) = OpenDisplaySection();
        await Row(rows, "Living Room TV").ClickAsync(new());

        // Still a live component with a working trigger: the throw was swallowed, not propagated
        // into the render, and the header did not lose its menu.
        Assert.NotEmpty(menu.FindAll(".kh-dropdown__trigger"));
    }

    // --- searching, and the two gaps it closes ---

    /// <summary>Asking the screens to discover opens a screen, which is not what pressing
    /// "search" asked for.</summary>
    [Fact]
    public async Task Searching_AsksOnlyTheTransportsThatActuallyLook()
    {
        var (_, rows) = OpenDisplaySection();
        await Row(rows, "Search for devices").ClickAsync(new());

        await _cast.Received(1).StartDiscoveryAsync(Arg.Any<CancellationToken>());
        await _screens.DidNotReceive().StartDiscoveryAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>Gap one: the label is the provider's own state, not a flag of ours. Discovery
    /// outlives the call that starts it, so a local flag says "searching" for the length of the
    /// first sweep and then lies for the rest of the night.</summary>
    [Fact]
    public void TheSearchRow_SaysItIsSearching_WhileTheProviderStillIs()
    {
        _cast.IsDiscovering.Returns(true);

        var (_, rows) = OpenDisplaySection();

        Assert.Contains(rows, r => r.TextContent.Contains("Stop searching", StringComparison.Ordinal));
        Assert.DoesNotContain(rows, r => r.TextContent.Contains("Search for devices", StringComparison.Ordinal));
    }

    /// <summary>Gap two: a sweep can be stopped. A console runs all night on whatever wifi the
    /// room has, and browsing is meant to be off until someone asks for it.</summary>
    [Fact]
    public async Task TheSearchRow_StopsTheSweep_WhenItIsAlreadyRunning()
    {
        _cast.IsDiscovering.Returns(true);

        var (_, rows) = OpenDisplaySection();
        await Row(rows, "Stop searching").ClickAsync(new());

        await _cast.Received(1).StopDiscoveryAsync(Arg.Any<CancellationToken>());
        await _cast.DidNotReceive().StartDiscoveryAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>The list fills in underneath, so closing the menu would hide what was asked for.</summary>
    [Fact]
    public async Task Searching_LeavesTheMenuOpen()
    {
        var (menu, rows) = OpenDisplaySection();
        await Row(rows, "Search for devices").ClickAsync(new());

        Assert.NotEmpty(menu.FindAll(".kh-settings-menu__flyout"));
    }

    [Fact]
    public void Describe_NamesTheTransportAndAddress()
        => Assert.Equal(
            "Chromecast · 192.168.1.14",
            SettingsButton.DescribeDisplay(_cast, Device("tv-1", "Living Room TV", address: "192.168.1.14")));

    [Fact]
    public void Describe_PrefersTheDeviceModelOverTheTransportName()
        => Assert.Equal(
            "This computer",
            SettingsButton.DescribeDisplay(_screens, Device("Screen 1", "Local Display", model: "This computer")));
}
