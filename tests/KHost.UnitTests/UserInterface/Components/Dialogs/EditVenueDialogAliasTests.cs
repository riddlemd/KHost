using Bunit;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.UserInterface.Components.Dialogs;
using Microsoft.Extensions.DependencyInjection;
using KHost.Domain.Services;
using KHost.Domain.Services.QrCodes;

namespace KHost.UnitTests.UserInterface.Components.Dialogs;

/// <summary>Controls whether the room hears the signed-up name or the singer's own name.</summary>
public class EditVenueDialogAliasTests : BunitContext
{
    private const string AliasSelector = "#venue-allow-aliases";

    private readonly IBreakMusicService _breakMusic = Substitute.For<IBreakMusicService>();
    private readonly IMediaPoolService _mediaPools = Substitute.For<IMediaPoolService>();

    public EditVenueDialogAliasTests()
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

    /// <summary>A venue never asked reads the missing key as off, and the switch has to agree.</summary>
    [Fact]
    public void VenueNeverAsked_OffersTheSwitchOff()
    {
        var cut = Render(new Venue.VenueSettings());

        Assert.False(cut.Find(AliasSelector).HasAttribute("checked"));
    }

    [Fact]
    public void VenueAllowingAliases_ShowsTheSwitchOn()
    {
        var cut = Render(new Venue.VenueSettings { AllowAliases = true });

        Assert.True(cut.Find(AliasSelector).HasAttribute("checked"));
    }

    /// <summary>Read into the model but never saved back, this looked fine yet never took effect.</summary>
    [Fact]
    public async Task SwitchingItOn_ReachesTheVenueThatIsSaved()
    {
        Venue? saved = null;
        var cut = Render(new Venue.VenueSettings(), venue => saved = venue);

        cut.Find(AliasSelector).Change(true);
        await cut.Find("form").SubmitAsync();

        Assert.NotNull(saved);
        Assert.True(saved!.Settings.AllowAliases);
    }

    [Fact]
    public async Task SwitchingItOff_ReachesTheVenueThatIsSaved()
    {
        Venue? saved = null;
        var cut = Render(new Venue.VenueSettings { AllowAliases = true }, venue => saved = venue);

        cut.Find(AliasSelector).Change(false);
        await cut.Find("form").SubmitAsync();

        Assert.NotNull(saved);
        Assert.False(saved!.Settings.AllowAliases);
    }

    /// <summary>The switch says what it does; a host turning it on is choosing what the room hears.</summary>
    [Fact]
    public void TheSwitch_ExplainsWhatItChanges()
    {
        var cut = Render(new Venue.VenueSettings());

        // Scoped to this switch's own row. The dialog is full of notes, and one belonging to
        // another control would pass this while saying nothing about the alias switch.
        var note = cut.Find($".kh-form-check:has({AliasSelector}) .kh-note");

        Assert.False(string.IsNullOrWhiteSpace(note.TextContent));
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
