using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Abstractions.Services.IPC;
using KHost.Domain.Services.Screens;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.Domain.Services;

/// <summary>Every state where the room hears something else takes the card down.</summary>
public class BreakMusicCardServiceTests
{
    private readonly IVenuesService _venues = Substitute.For<IVenuesService>();
    private readonly IBreakMusicService _breakMusic = Substitute.For<IBreakMusicService>();

    // Through a container, because break music reaches every provider a plugin registered and a
    // plugin is one instance pointed at each extension interface it implements.
    private BreakMusicCardService Service() => new(
        NullLogger<BreakMusicCardService>.Instance, _venues,
        new ServiceCollection().AddSingleton(_breakMusic).BuildServiceProvider());

    private void Arrange(
        bool enabled = true,
        BreakMusicState state = BreakMusicState.Playing,
        string? title = "Free Fallin'",
        string artist = "Tom Petty",
        ScreenCorner? corner = null,
        double offset = 0)
    {
        _venues.ReadSelectedVenueAsync().Returns(new Venue
        {
            Name = "The Bar",
            Settings = new Venue.VenueSettings
            {
                BreakMusicCardEnabled = enabled,
                BreakMusicCardCorner = corner,
                QrCodeOffset = offset,
            },
        });

        _breakMusic.State.Returns(state);
        _breakMusic.CurrentTrack.Returns(title is null ? null : new BreakMusicTrack { Title = title, Artist = artist });
    }

    [Fact]
    public async Task BuildAsync_Playing_NamesTheTrackAndTheArtist()
    {
        Arrange();

        var command = await Service().BuildAsync();

        Assert.True(command.Enabled);
        Assert.Equal("Free Fallin'", command.Title);
        Assert.Equal("Tom Petty", command.Artist);
    }

    /// <summary>A host who paused meant it, and the room is not hearing this.</summary>
    [Fact]
    public async Task BuildAsync_Paused_SaysNothing()
    {
        Arrange(state: BreakMusicState.Paused);

        Assert.False((await Service().BuildAsync()).Enabled);
    }

    /// <summary>Suspended is break music standing aside; the card must not name it over a singer.</summary>
    [Fact]
    public async Task BuildAsync_SuspendedForASinger_SaysNothing()
    {
        Arrange(state: BreakMusicState.Suspended);

        Assert.False((await Service().BuildAsync()).Enabled);
    }

    [Fact]
    public async Task BuildAsync_Stopped_SaysNothing()
    {
        Arrange(state: BreakMusicState.Stopped);

        Assert.False((await Service().BuildAsync()).Enabled);
    }

    /// <summary>The venue's choice beats whatever is playing.</summary>
    [Fact]
    public async Task BuildAsync_VenueTurnedItOff_SaysNothingWhilePlaying()
    {
        Arrange(enabled: false);

        Assert.False((await Service().BuildAsync()).Enabled);
    }

    /// <summary>No venue is nobody to have asked, so it is the same answer rather than a default.</summary>
    [Fact]
    public async Task BuildAsync_NoVenueSelected_SaysNothing()
    {
        _venues.ReadSelectedVenueAsync().Returns((Venue?)null);
        _breakMusic.State.Returns(BreakMusicState.Playing);

        Assert.False((await Service().BuildAsync()).Enabled);
    }

    /// <summary>A provider driving another app need not report an artist.</summary>
    [Fact]
    public async Task BuildAsync_NoArtistReported_NamesTheTrackAlone()
    {
        Arrange(artist: "");

        var command = await Service().BuildAsync();

        Assert.True(command.Enabled);
        Assert.Null(command.Artist);
    }

    /// <summary>A provider with nothing to say has nothing worth a corner of the picture.</summary>
    [Fact]
    public async Task BuildAsync_NoTitleReported_SaysNothing()
    {
        Arrange(title: "");

        Assert.False((await Service().BuildAsync()).Enabled);
    }

    /// <summary>Away from the codes' own default, so the two do not share a corner uninvited.</summary>
    [Fact]
    public async Task BuildAsync_VenueNeverChoseACorner_TakesBottomLeft()
    {
        Arrange();

        Assert.Equal(ScreenCorner.BottomLeft, (await Service().BuildAsync()).Corner);
    }

    [Fact]
    public async Task BuildAsync_VenueChoseACorner_UsesIt()
    {
        Arrange(corner: ScreenCorner.TopRight);

        Assert.Equal(ScreenCorner.TopRight, (await Service().BuildAsync()).Corner);
    }

    /// <summary>The inset belongs to the corner, not what sits in it: a card and a code must agree.</summary>
    [Fact]
    public async Task BuildAsync_VenueSetAnInset_SharesItWithTheCodes()
    {
        Arrange(offset: 6.5);

        Assert.Equal(6.5, (await Service().BuildAsync()).Offset, 3);
    }

    [Fact]
    public async Task BuildAsync_VenueNeverSetAnInset_TakesTheHostsOwn()
    {
        Arrange(offset: 0);

        Assert.Equal(0.2, (await Service().BuildAsync()).Offset, 3);
    }
}
