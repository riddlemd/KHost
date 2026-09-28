using Bunit;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.UserInterface.Components.Setup;
using Microsoft.Extensions.DependencyInjection;

namespace KHost.UnitTests.UserInterface.Components.Setup;

/// <summary>The wizard's own venue, one of the three venue-creation paths that starts a venue
/// pointed at the built-in visualisation playlist rather than black.</summary>
public class WizardStep2VenueSetupTests : BunitContext
{
    private readonly IVenuesService _venuesService = Substitute.For<IVenuesService>();

    public WizardStep2VenueSetupTests()
    {
        Services.AddSingleton(_venuesService);
        _venuesService.CreateAsync(Arg.Any<Venue>()).Returns(c => c.Arg<Venue>());
    }

    [Fact]
    public async Task OnNext_CreatesTheVenue_OnTheDefaultVisualisationPlaylist()
    {
        var cut = Render<WizardStep2VenueSetup>();

        await cut.InvokeAsync(() => cut.Find("form").Submit());

        await _venuesService.Received(1).CreateAsync(
            Arg.Is<Venue>(v => v.Settings.VisualisationPlaylistId == VisualisationPlaylist.DefaultId));
    }
}
