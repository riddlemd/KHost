using Bunit;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.UserInterface.Components.Dialogs;
using Microsoft.Extensions.DependencyInjection;
using KHost.Domain.Services;
using KHost.Domain.Services.QrCodes;

namespace KHost.UnitTests.UserInterface.Components.Dialogs;

/// <summary>Turns away a song another singer already has queued.</summary>
public class EditVenueDialogRefuseSongQueuedForAnotherSingerTests : BunitContext
{
    private const string RefuseSelector = "#venue-refuse-song-queued-for-another-singer";

    private readonly IBreakMusicService _breakMusic = Substitute.For<IBreakMusicService>();
    private readonly IMediaPoolService _mediaPools = Substitute.For<IMediaPoolService>();

    public EditVenueDialogRefuseSongQueuedForAnotherSingerTests()
    {
        this.AddKHostTestDefaults();

        Services.AddAppSettings();

        _mediaPools.ReadAllWithEntriesAsync(Arg.Any<PoolPurpose>(), Arg.Any<Guid?>())
            .Returns(new List<MediaPool>());
        _breakMusic.Providers.Returns(new List<IBreakMusicProvider>());

        Services.AddSingleton(_breakMusic);
        Services.AddSingleton(Substitute.For<IQrCodePngExporter>());
        Services.AddSingleton(Substitute.For<IMediaUploader>());
        Services.AddSingleton(_mediaPools);

        // The dialog reads the visualisation playlists as it opens; none is all these need.
        var visualisations = Substitute.For<IVisualisationPlaylistService>();
        visualisations.ReadAllWithEntriesAsync().Returns(new List<VisualisationPlaylist>());
        Services.AddSingleton(visualisations);

        // The dialog reads the venue's background folder on open; an empty pack is the
        // shape a venue that has never chosen one has.

        var plugins = Substitute.For<IPluginRegistry>();
        plugins.Plugins.Returns([]);
        Services.AddSingleton(plugins);
    }

    /// <summary>A venue saved before the setting existed reads the missing key as off.</summary>
    [Fact]
    public void VenueNeverAsked_OffersTheSwitchOff()
    {
        var cut = Render(new Venue.VenueSettings());

        Assert.False(cut.Find(RefuseSelector).HasAttribute("checked"));
    }

    [Fact]
    public async Task SwitchingItOn_ReachesTheVenueThatIsSaved()
    {
        Venue? saved = null;
        var cut = Render(new Venue.VenueSettings(), venue => saved = venue);

        cut.Find(RefuseSelector).Change(true);
        await cut.Find("form").SubmitAsync();

        Assert.NotNull(saved);
        Assert.True(saved!.Settings.RefuseSongQueuedForAnotherSinger);
    }

    [Fact]
    public async Task SwitchingItOff_ReachesTheVenueThatIsSaved()
    {
        Venue? saved = null;
        var cut = Render(new Venue.VenueSettings { RefuseSongQueuedForAnotherSinger = true }, venue => saved = venue);

        cut.Find(RefuseSelector).Change(false);
        await cut.Find("form").SubmitAsync();

        Assert.NotNull(saved);
        Assert.False(saved!.Settings.RefuseSongQueuedForAnotherSinger);
    }

    private IRenderedComponent<EditVenueDialog> Render(
        Venue.VenueSettings settings, Action<Venue>? onSave = null)
        => Render<EditVenueDialog>(ps =>
        {
            ps.Add(p => p.IsOpen, true)
              .Add(p => p.Venue, new Venue { Name = "Test Venue", Settings = settings });

            if (onSave is not null)
                ps.Add(p => p.OnSave, onSave);
        });
}
