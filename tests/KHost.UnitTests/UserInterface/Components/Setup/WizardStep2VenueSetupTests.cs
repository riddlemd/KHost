using Bunit;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.DataAccess.Services;
using KHost.UserInterface.Components.Setup;
using KHost.UserInterface.Models;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;

namespace KHost.UnitTests.UserInterface.Components.Setup;

/// <summary>The wizard's own venue, one of the venue-creation paths: it starts on the playlist the
/// Backgrounds step saved, rather than black.</summary>
public class WizardStep2VenueSetupTests : BunitContext
{
    private readonly IVenuesService _venuesService = Substitute.For<IVenuesService>();
    private readonly IAppSettingsService _appSettings = Substitute.For<IAppSettingsService>();

    public WizardStep2VenueSetupTests()
    {
        Services.AddSingleton(_venuesService);
        Services.AddSingleton(_appSettings);
        _venuesService.CreateAsync(Arg.Any<Venue>()).Returns(c => c.Arg<Venue>());
    }

    [Theory]
    [InlineData(VenueBackgrounds.Basic)]
    [InlineData(VenueBackgrounds.Advanced)]
    public async Task OnNext_CreatesTheVenue_OnThePlaylistAppSettingsNames(VenueBackgrounds chosen)
    {
        _appSettings.Current.Returns(new AppSettings { NewVenueBackgrounds = chosen });
        var expected = chosen == VenueBackgrounds.Advanced ? ShippedVisualisationPlaylists.AdvancedId : ShippedVisualisationPlaylists.BasicId;
        var cut = Render<WizardStep2VenueSetup>();

        await cut.InvokeAsync(() => cut.Find("form").Submit());

        await _venuesService.Received(1).CreateAsync(Arg.Is<Venue>(v => v.Settings.VisualisationPlaylistId == expected));
    }

    [Fact]
    public async Task OnNext_CreatesTheVenue_WithThePlaceholderImageAppSettingsNames()
    {
        var image = Guid.NewGuid();
        _appSettings.Current.Returns(new AppSettings { NewVenuePlaceholderImageId = image, NewVenuePlaceholderImageScaling = ImageScaling.Fill });
        var cut = Render<WizardStep2VenueSetup>();

        await cut.InvokeAsync(() => cut.Find("form").Submit());

        await _venuesService.Received(1).CreateAsync(Arg.Is<Venue>(v =>
            v.Settings.BrandingImageMediaId == image && v.Settings.BrandingImageScaling == ImageScaling.Fill));
    }
}
