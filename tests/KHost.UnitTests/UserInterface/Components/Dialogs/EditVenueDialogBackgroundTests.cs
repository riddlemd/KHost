using Bunit;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services.Messaging;
using KHost.UserInterface.Components.Dialogs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.UserInterface.Components.Dialogs;

/// <summary>What goes behind a song's words: the visualiser, or black. There is no picker of
/// background clips any more.</summary>
public class EditVenueDialogBackgroundTests : BunitContext
{
    private readonly IBreakMusicService _breakMusic = Substitute.For<IBreakMusicService>();
    private readonly IMediaPoolService _mediaPools = Substitute.For<IMediaPoolService>();
    private readonly IMediaService _media = Substitute.For<IMediaService>();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);

    public EditVenueDialogBackgroundTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        _mediaPools.ReadAllWithEntriesAsync(Arg.Any<PoolPurpose>(), Arg.Any<Guid?>())
            .Returns(new List<MediaPool>());
        _breakMusic.Providers.Returns(new List<IBreakMusicProvider>());

        Services.AddSingleton(_breakMusic);
        Services.AddSingleton(_mediaPools);
        Services.AddSingleton(_media);
        Services.AddSingleton<IMessageBroker>(_broker);

        var plugins = Substitute.For<IPluginRegistry>();
        plugins.Plugins.Returns([]);
        Services.AddSingleton(plugins);
    }

    /// <summary>A venue stored before the retired list existed deserialises without it; the dialog
    /// must still open.</summary>
    [Fact]
    public void AVenueStoredBeforeTheSettingExisted_OpensRatherThanThrowing()
    {
#pragma warning disable CS0618 // the retired setting's null handling is what this checks
        var settings = new Venue.VenueSettings { SongBackgrounds = null! };

        var cut = Render(settings);

        Assert.Empty(settings.SongBackgrounds);
#pragma warning restore CS0618
        Assert.NotNull(cut.Find("form"));
    }

    [Fact]
    public void TheSection_OffersNoBackgroundClips()
    {
#pragma warning disable CS0618 // a venue that picked clips before the picker went
        var cut = Render(new Venue.VenueSettings { SongBackgrounds = ["amber.mp4"] });
#pragma warning restore CS0618

        Assert.Empty(cut.FindAll(".kh-background-tile"));
        Assert.Empty(cut.FindAll("[id^='venue-background-']"));
        Assert.DoesNotContain("plain black", cut.Markup);
        Assert.DoesNotContain("background-still", cut.Markup);
        Assert.NotNull(cut.Find("#venue-song-visualiser"));
    }

    /// <summary>Nothing edits them now, so whatever a venue stored is saved back untouched.</summary>
    [Fact]
    public void TheRetiredBackgrounds_SurviveASave()
    {
        Venue? saved = null;
#pragma warning disable CS0618 // the retired setting is what this checks
        var cut = Render(new Venue.VenueSettings { SongBackgrounds = ["amber.mp4"] }, venue => saved = venue);

        cut.Find("form").Submit();

        Assert.Equal(["amber.mp4"], saved!.Settings.SongBackgrounds);
#pragma warning restore CS0618
    }

    [Fact]
    public void TheVisualiser_IsOffForAVenueThatWasNeverAsked()
        => Assert.False(Render(new Venue.VenueSettings()).Find("#venue-song-visualiser").HasAttribute("checked"));

    [Fact]
    public void TickingTheVisualiser_ReachesTheVenueThatIsSaved()
    {
        Venue? saved = null;
        var cut = Render(new Venue.VenueSettings(), venue => saved = venue);

        cut.Find("#venue-song-visualiser").Change(true);
        cut.Find("form").Submit();

        Assert.True(saved!.Settings.SongVisualiserEnabled);
    }

    [Fact]
    public void UntickingTheVisualiser_ReachesTheVenueThatIsSaved()
    {
        Venue? saved = null;
        var cut = Render(new Venue.VenueSettings { SongVisualiserEnabled = true }, venue => saved = venue);

        Assert.True(cut.Find("#venue-song-visualiser").HasAttribute("checked"));

        cut.Find("#venue-song-visualiser").Change(false);
        cut.Find("form").Submit();

        Assert.False(saved!.Settings.SongVisualiserEnabled);
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
