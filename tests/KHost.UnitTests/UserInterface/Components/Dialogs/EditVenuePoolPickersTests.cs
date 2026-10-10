using Bunit;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.UserInterface.Components.Dialogs;
using KHost.UserInterface.Models;
using Microsoft.Extensions.DependencyInjection;

namespace KHost.UnitTests.UserInterface.Components.Dialogs;

/// <summary>A deleted playlist's id left on a venue read as "none" in the dialog, and saving kept
/// it: there was no way to clear what the field said was already clear.</summary>
public class EditVenuePoolPickersTests : BunitContext
{
    private readonly IMediaPoolService _pools = Substitute.For<IMediaPoolService>();
    private readonly IBreakMusicService _breakMusic = Substitute.For<IBreakMusicService>();
    private readonly MediaPool _live = new() { Id = Guid.NewGuid(), Name = "Spots" };

    public EditVenuePoolPickersTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        _pools.ReadAllWithEntriesAsync(Arg.Any<PoolPurpose>(), Arg.Any<Guid?>())
            .Returns(new List<MediaPool> { _live });

        // The playlist picker shows only while the host's own playlists feed break music.
        var library = Substitute.For<IBreakMusicProvider>();
        _breakMusic.ActiveProvider.Returns(library);
        _breakMusic.LibraryProvider.Returns(library);

        Services.AddSingleton(_pools);
        Services.AddSingleton(_breakMusic);
    }

    [Fact]
    public void Ads_AnIdThatNoLongerResolves_IsDroppedSoSavingClearsIt()
    {
        var model = new EditVenueModel { AdPoolId = Guid.NewGuid() };

        Render<EditVenueAds>(ps => ps.Add(p => p.Model, model));

        Assert.Null(model.AdPoolId);
    }

    [Fact]
    public void Ads_AnIdThatResolves_IsKept()
    {
        var model = new EditVenueModel { AdPoolId = _live.Id };

        Render<EditVenueAds>(ps => ps.Add(p => p.Model, model));

        Assert.Equal(_live.Id, model.AdPoolId);
    }

    [Fact]
    public void BreakMusic_AnIdThatNoLongerResolves_IsDroppedSoSavingClearsIt()
    {
        var model = new EditVenueModel { BreakMusicPoolId = Guid.NewGuid() };

        Render<EditVenueBreakMusic>(ps => ps.Add(p => p.Model, model));

        Assert.Null(model.BreakMusicPoolId);
    }

    [Fact]
    public void BreakMusic_AnIdThatResolves_IsKept()
    {
        var model = new EditVenueModel { BreakMusicPoolId = _live.Id };

        Render<EditVenueBreakMusic>(ps => ps.Add(p => p.Model, model));

        Assert.Equal(_live.Id, model.BreakMusicPoolId);
    }
}
