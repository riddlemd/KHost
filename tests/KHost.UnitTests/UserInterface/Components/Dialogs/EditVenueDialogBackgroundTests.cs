using Bunit;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.UserInterface.Components.Dialogs;
using Microsoft.Extensions.DependencyInjection;
using KHost.Domain.Services;
using KHost.Domain.Services.QrCodes;
using KHost.DataAccess.Services;
using KHost.UserInterface.Models;
using KHost.UserInterface.Services;

namespace KHost.UnitTests.UserInterface.Components.Dialogs;

/// <summary>What goes behind a song's words: a visualisation playlist, or black. There is no picker
/// of background clips any more.</summary>
public class EditVenueDialogBackgroundTests : BunitContext
{
    private readonly IBreakMusicService _breakMusic = Substitute.For<IBreakMusicService>();
    private readonly IMediaPoolService _mediaPools = Substitute.For<IMediaPoolService>();
    private readonly IVisualisationPlaylistService _visualisations = Substitute.For<IVisualisationPlaylistService>();
    private readonly VisualisationPlaylist _night = new() { Name = "Night" };
    private readonly VisualisationPlaylist _party = new() { Name = "Party" };

    public EditVenueDialogBackgroundTests()
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

        _visualisations.ReadAllWithEntriesAsync().Returns(new List<VisualisationPlaylist> { _night, _party });
        Services.AddSingleton(_visualisations);

        var plugins = Substitute.For<IPluginRegistry>();
        plugins.Plugins.Returns([]);
        Services.AddSingleton(plugins);
    }

    [Fact]
    public void TheSection_OffersNoBackgroundClips()
    {
        var cut = Render(new Venue.VenueSettings());

        Assert.Empty(cut.FindAll(".kh-background-tile"));
        Assert.Empty(cut.FindAll("[id^='venue-background-']"));
        Assert.DoesNotContain("plain black", cut.Markup);
        Assert.DoesNotContain("background-still", cut.Markup);
        Assert.NotNull(VisualisationPicker(cut));
    }

    [Fact]
    public void ThePlaylist_IsNoneForAVenueThatWasNeverAsked()
        => Assert.Equal("", VisualisationPicker(Render(new Venue.VenueSettings())).GetAttribute("value"));

    [Fact]
    public void ThePlaylist_ShowsTheOneTheVenuePicked()
        => Assert.Equal("Party", VisualisationPicker(Render(new Venue.VenueSettings { VisualisationPlaylistId = _party.Id })).GetAttribute("value"));

    [Fact]
    public async Task PickingAPlaylist_ReachesTheVenueThatIsSaved()
    {
        Venue? saved = null;
        var cut = Render(new Venue.VenueSettings(), venue => saved = venue);

        VisualisationPicker(cut).Focus();
        cut.FindAll(".kh-combobox__option").Single(option => option.TextContent == "Night").Click();
        await cut.Find("form").SubmitAsync();

        Assert.Equal(_night.Id, saved!.Settings.VisualisationPlaylistId);
    }

    [Fact]
    public async Task ClearingThePlaylist_ReachesTheVenueThatIsSaved()
    {
        Venue? saved = null;
        var cut = Render(new Venue.VenueSettings { VisualisationPlaylistId = _night.Id }, venue => saved = venue);

        VisualisationPicker(cut).Input("");
        // Submit() does not wait: under load it queues behind the picker's debounced search and returns unsaved.
        await cut.Find("form").SubmitAsync();

        Assert.Null(saved!.Settings.VisualisationPlaylistId);
    }

    [Theory]
    [InlineData(VenueBackgrounds.Basic, "Basic Backgrounds")]
    [InlineData(VenueBackgrounds.Advanced, "Advanced Backgrounds")]
    public void ANewVenue_StartsOnThePlaylistAppSettingsNames(VenueBackgrounds chosen, string expected)
    {
        _visualisations.ReadAllWithEntriesAsync().Returns(new List<VisualisationPlaylist>
        {
            new() { Id = ShippedVisualisationPlaylists.BasicId, Name = "Basic Backgrounds" },
            new() { Id = ShippedVisualisationPlaylists.AdvancedId, Name = "Advanced Backgrounds" },
        });
        Services.AddAppSettings(new AppSettings { NewVenueBackgrounds = chosen });

        var cut = Render<EditVenueDialog>(ps => ps.Add(p => p.IsOpen, true).Add(p => p.Venue, (Venue?)null));

        Assert.Equal(expected, VisualisationPicker(cut).GetAttribute("value"));
    }

    private static AngleSharp.Dom.IElement VisualisationPicker(IRenderedComponent<EditVenueDialog> cut)
        => cut.FindAll(".kh-venue-settings__section")
            .Single(section => section.TextContent.Contains("Visualisation playlist"))
            .QuerySelector(".kh-combobox__input")!;

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
