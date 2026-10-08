using Bunit;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services.Messaging;
using KHost.UserInterface.Components.Dialogs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using KHost.Domain.Services;
using KHost.Domain.Services.QrCodes;

namespace KHost.UnitTests.UserInterface.Components.Dialogs;

/// <summary>What goes behind a song's words: a visualisation playlist, or black. There is no picker
/// of background clips any more.</summary>
public class EditVenueDialogBackgroundTests : BunitContext
{
    private readonly IBreakMusicService _breakMusic = Substitute.For<IBreakMusicService>();
    private readonly IMediaPoolService _mediaPools = Substitute.For<IMediaPoolService>();
    private readonly IMediaService _media = Substitute.For<IMediaService>();
    private readonly IVisualisationPlaylistService _visualisations = Substitute.For<IVisualisationPlaylistService>();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);
    private readonly VisualisationPlaylist _night = new() { Name = "Night" };
    private readonly VisualisationPlaylist _party = new() { Name = "Party" };

    public EditVenueDialogBackgroundTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        _mediaPools.ReadAllWithEntriesAsync(Arg.Any<PoolPurpose>(), Arg.Any<Guid?>())
            .Returns(new List<MediaPool>());
        _breakMusic.Providers.Returns(new List<IBreakMusicProvider>());

        Services.AddSingleton(_breakMusic);
        Services.AddSingleton(Substitute.For<IQrCodePngExporter>());
        Services.AddSingleton(Substitute.For<IMediaUploader>());
        Services.AddSingleton(Substitute.For<IFlashService>());
        Services.AddSingleton(_mediaPools);
        Services.AddSingleton(_media);
        Services.AddSingleton<IMessageBroker>(_broker);

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
    public void PickingAPlaylist_ReachesTheVenueThatIsSaved()
    {
        Venue? saved = null;
        var cut = Render(new Venue.VenueSettings(), venue => saved = venue);

        VisualisationPicker(cut).Focus();
        cut.FindAll(".kh-combobox__option").Single(option => option.TextContent == "Night").Click();
        cut.Find("form").Submit();

        Assert.Equal(_night.Id, saved!.Settings.VisualisationPlaylistId);
    }

    [Fact]
    public void ClearingThePlaylist_ReachesTheVenueThatIsSaved()
    {
        Venue? saved = null;
        var cut = Render(new Venue.VenueSettings { VisualisationPlaylistId = _night.Id }, venue => saved = venue);

        VisualisationPicker(cut).Input("");
        cut.Find("form").Submit();

        Assert.Null(saved!.Settings.VisualisationPlaylistId);
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
