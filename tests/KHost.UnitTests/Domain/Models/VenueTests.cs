using KHost.Abstractions.Models;

namespace KHost.UnitTests.Domain.Models;

public class VenueTests
{
    [Fact]
    public void Venue_DefaultsToEnabled()
    {
        var venue = new Venue { Name = "The Pub" };

        Assert.True(venue.Enabled);
    }

    [Fact]
    public void Venue_InitializesWithNewGuidId()
    {
        var venue = new Venue { Name = "The Pub" };

        Assert.NotEqual(Guid.Empty, venue.Id);
    }

    [Fact]
    public void Venue_NotesDefaultsToEmptyString()
    {
        var venue = new Venue { Name = "The Pub" };

        Assert.Equal("", venue.Notes);
    }

}
