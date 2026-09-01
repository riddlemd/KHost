using AngleSharp.Dom;
using Bunit;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services.Messaging;
using KHost.Abstractions.Messaging;
using KHost.UserInterface.Components;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.UserInterface.Components;

/// <summary>
/// Two pages only mean anything under a venue setting, so the menu drops them rather than offering
/// a page that describes someone else's music or lists tips a venue does not take.
/// </summary>
public class SettingsButtonPageVisibilityTests : BunitContext
{
    private readonly IVenuesService _venues = Substitute.For<IVenuesService>();
    private readonly IBreakMusicService _breakMusic = Substitute.For<IBreakMusicService>();
    private readonly IPermissionService _permissions = Substitute.For<IPermissionService>();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);

    public SettingsButtonPageVisibilityTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        _permissions.IsAdminAsync().Returns(true);
        _permissions.HasAsync(Arg.Any<KHostPermission>()).Returns(true);

        _venues.ReadAllAsync(Arg.Any<int>(), Arg.Any<int>()).Returns(new PaginatedResult<Venue>());

        var appSettings = Substitute.For<IAppSettingsService>();
        appSettings.Current.Returns(new AppSettings());

        Services.AddSingleton(_venues);
        Services.AddSingleton(_breakMusic);
        Services.AddSingleton(_permissions);
        Services.AddSingleton(appSettings);
        Services.AddSingleton(Substitute.For<IDialogService>());
        Services.AddSingleton(Substitute.For<IThemeService>());
        Services.AddSingleton<IMessageBroker>(_broker);
    }

    [Fact]
    public void BreakMusicManager_ShowsWhenTheVenuePlaysTheLibrarysOwnPlaylists()
    {
        ArrangeVenue(tipping: true);
        ArrangeBreakMusic(activeIsLibrary: true);

        Assert.Contains(MenuItems(), i => i.TextContent.Contains("Break Music Manager"));
    }

    /// <summary>
    /// The other provider renders through the host on purpose: the page follows what feeds the
    /// playlists, not who plays the audio.
    /// </summary>
    [Fact]
    public void BreakMusicManager_GoesWhenTheVenuesModeBringsItsOwnMusic()
    {
        ArrangeVenue(tipping: true);
        ArrangeBreakMusic(activeIsLibrary: false);

        Assert.DoesNotContain(MenuItems(), i => i.TextContent.Contains("Break Music Manager"));
    }

    [Fact]
    public void TipsManager_ShowsWhenTheVenueTakesTips()
    {
        ArrangeVenue(tipping: true);
        ArrangeBreakMusic(activeIsLibrary: true);

        Assert.Contains(MenuItems(), i => i.TextContent.Contains("Tips Manager"));
    }

    [Fact]
    public void TipsManager_GoesWhenTheVenueDoesNotTakeTips()
    {
        ArrangeVenue(tipping: false);
        ArrangeBreakMusic(activeIsLibrary: true);

        Assert.DoesNotContain(MenuItems(), i => i.TextContent.Contains("Tips Manager"));
    }

    /// <summary>Nothing for a tip to belong to yet, and no venue naming a break music mode.</summary>
    [Fact]
    public void NoVenueSelected_DropsBoth()
    {
        _venues.ReadSelectedVenueAsync().Returns(Task.FromResult<Venue?>(null));
        ArrangeBreakMusic(activeIsLibrary: true);

        var items = MenuItems();

        Assert.DoesNotContain(items, i => i.TextContent.Contains("Tips Manager"));
        // A page every venue has is still there, so this is not an empty menu passing by accident.
        Assert.Contains(items, i => i.TextContent.Contains("Users Manager"));
    }

    private void ArrangeVenue(bool tipping)
    {
        var venue = new Venue { Name = "Test Venue" };
        venue.Settings.TippingEnabled = tipping;

        _venues.ReadSelectedVenueAsync().Returns(venue);
    }

    private void ArrangeBreakMusic(bool activeIsLibrary)
    {
        var library = Provider("Library", "LibraryBreakMusicProvider");
        var other = Provider("Jukebox", "JukeboxProvider");

        _breakMusic.LibraryProvider.Returns(library);
        _breakMusic.Providers.Returns(new List<IBreakMusicProvider> { library, other });
        _breakMusic.ActiveProvider.Returns(activeIsLibrary ? library : other);
    }

    private static IBreakMusicProvider Provider(string displayName, string sourceName)
    {
        var provider = Substitute.For<IBreakMusicProvider>();

        provider.DisplayName.Returns(displayName);
        provider.SourceName.Returns(sourceName);
        // True for both: the page follows the playlists, so this must not be what decides it.
        provider.RendersThroughHost.Returns(true);

        return provider;
    }

    private IReadOnlyList<IElement> MenuItems()
    {
        var menu = Render<SettingsButton>();
        menu.Find(".kh-dropdown__trigger").Click();

        return menu.FindAll(".kh-dropdown__item");
    }
}
