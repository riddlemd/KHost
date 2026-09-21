using Bunit;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Models;
using KHost.Abstractions.Models.Backgrounds;
using KHost.Abstractions.Services;
using KHost.Domain.Services.Messaging;
using KHost.UserInterface.Components.Dialogs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.UserInterface.Components.Dialogs;

/// <summary>Which backgrounds a song may be given. Ticking none is the black background, so the
/// grid is the whole control — there is no separate switch that could disagree with it.</summary>
public class EditVenueDialogBackgroundTests : BunitContext
{
    private const string Amber = "#venue-background-amber-mp4";
    private const string Violet = "#venue-background-violet-mp4";

    private readonly IBreakMusicService _breakMusic = Substitute.For<IBreakMusicService>();
    private readonly IMediaPoolService _mediaPools = Substitute.For<IMediaPoolService>();
    private readonly IMediaService _media = Substitute.For<IMediaService>();
    private readonly IBackgroundPackService _packs = Substitute.For<IBackgroundPackService>();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);

    public EditVenueDialogBackgroundTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        _mediaPools.ReadAllWithEntriesAsync(Arg.Any<PoolPurpose>(), Arg.Any<Guid?>())
            .Returns(new List<MediaPool>());
        _breakMusic.Providers.Returns(new List<IBreakMusicProvider>());
        _packs.ReadAsync(Arg.Any<CancellationToken>()).Returns(new BackgroundPack());

        Services.AddSingleton(_breakMusic);
        Services.AddSingleton(_mediaPools);
        Services.AddSingleton(_media);
        Services.AddSingleton(_packs);
        Services.AddSingleton<IMessageBroker>(_broker);

        var plugins = Substitute.For<IPluginRegistry>();
        plugins.Plugins.Returns([]);
        Services.AddSingleton(plugins);
    }

    private static BackgroundPackEntry Entry(string file, bool still = true) => new()
    {
        File = file,
        Name = Path.GetFileNameWithoutExtension(file),
        FilePath = "/packs/" + file,
        StillPath = still ? "/packs/" + Path.GetFileNameWithoutExtension(file) + ".jpg" : null,
    };

    private void PackHolds(params BackgroundPackEntry[] entries)
        => _packs.ReadAsync(Arg.Any<CancellationToken>())
            .Returns(new BackgroundPack { Entries = entries });

    private void PackFails(BackgroundPackProblem problem)
        => _packs.ReadAsync(Arg.Any<CancellationToken>())
            .Returns(new BackgroundPack { Problem = problem });

    /// <summary>A venue stored before this setting existed deserialises without it, and the list
    /// arrived null rather than empty — which threw as the dialog opened, taking the whole venue
    /// out of reach rather than just the backgrounds.</summary>
    [Fact]
    public void AVenueStoredBeforeTheSettingExisted_OpensRatherThanThrowing()
    {
        var settings = new Venue.VenueSettings { SongBackgrounds = null! };

        var cut = Render(settings);

        Assert.Empty(settings.SongBackgrounds);
        Assert.NotNull(cut.Find("form"));
    }

    /// <summary>With nothing installed and nothing configured there is simply nothing to tick, and
    /// that is not an error the venue can act on.</summary>
    [Fact]
    public void NoBackgroundsAnywhere_SaysSoWithoutComplaining()
    {
        var cut = Render(new Venue.VenueSettings());

        Assert.Empty(cut.FindAll(".kh-background-tile"));
        Assert.Contains("No backgrounds to choose from", cut.Markup);
        Assert.Empty(cut.FindAll(".kh-note--danger"));
    }

    /// <summary>The machine's folder is on a disk that gets rebuilt, and the venue is not where it
    /// is fixed — so this points at App Settings rather than complaining here.</summary>
    [Fact]
    public void TheMachinesFolderIsGone_PointsAtAppSettingsWithoutBlockingTheDialog()
    {
        PackFails(BackgroundPackProblem.FolderMissing);

        var cut = Render(new Venue.VenueSettings());

        Assert.Contains("App Settings", cut.Find(".kh-note--warning").TextContent);
        Assert.NotNull(cut.Find("form"));
    }

    /// <summary>A folder that is gone must not take the ones KHost ships with it.</summary>
    [Fact]
    public void TheMachinesFolderIsGone_StillOffersWhateverWasRead()
    {
        _packs.ReadAsync(Arg.Any<CancellationToken>()).Returns(new BackgroundPack
        {
            Problem = BackgroundPackProblem.FolderMissing,
            Entries = [Entry("shipped.mp4")],
        });

        Assert.Single(Render(new Venue.VenueSettings()).FindAll(".kh-background-tile"));
    }



    [Fact]
    public void EachBackgroundInTheFolder_GetsATile()
    {
        PackHolds(Entry("amber.mp4"), Entry("violet.mp4"));

        var cut = Render(new Venue.VenueSettings());

        Assert.Equal(2, cut.FindAll(".kh-background-tile").Count);
        Assert.NotNull(cut.Find(Amber));
        Assert.NotNull(cut.Find(Violet));
    }

    [Fact]
    public void TheVenuesChoices_ComeBackTicked()
    {
        PackHolds(Entry("amber.mp4"), Entry("violet.mp4"));

        var cut = Render(new Venue.VenueSettings
        {
            SongBackgrounds = ["amber.mp4"],
        });

        Assert.True(cut.Find(Amber).HasAttribute("checked"));
        Assert.False(cut.Find(Violet).HasAttribute("checked"));
    }

    /// <summary>Read into the model but never saved back, this would look right and never take.
    /// </summary>
    [Fact]
    public void TickingABackground_ReachesTheVenueThatIsSaved()
    {
        PackHolds(Entry("amber.mp4"), Entry("violet.mp4"));
        Venue? saved = null;
        var cut = Render(new Venue.VenueSettings(), venue => saved = venue);

        cut.Find(Amber).Change(true);
        cut.Find("form").Submit();

        Assert.NotNull(saved);
        Assert.Equal(["amber.mp4"], saved!.Settings.SongBackgrounds);
    }

    [Fact]
    public void UntickingABackground_ReachesTheVenueThatIsSaved()
    {
        PackHolds(Entry("amber.mp4"), Entry("violet.mp4"));
        Venue? saved = null;
        var cut = Render(new Venue.VenueSettings
        {
            SongBackgrounds = ["amber.mp4", "violet.mp4"],
        }, venue => saved = venue);

        cut.Find(Amber).Change(false);
        cut.Find("form").Submit();

        Assert.NotNull(saved);
        Assert.Equal(["violet.mp4"], saved!.Settings.SongBackgrounds);
    }

    /// <summary>A folder that is temporarily unreachable must not silently empty a venue's
    /// choices, so a name nothing matches is left alone rather than tidied away.</summary>
    [Fact]
    public void AChoiceNoLongerInTheFolder_SurvivesASave()
    {
        PackHolds(Entry("amber.mp4"));
        Venue? saved = null;
        var cut = Render(new Venue.VenueSettings
        {
            SongBackgrounds = ["amber.mp4", "retired.mp4"],
        }, venue => saved = venue);

        cut.Find("form").Submit();

        Assert.NotNull(saved);
        Assert.Contains("retired.mp4", saved!.Settings.SongBackgrounds);
    }

    [Fact]
    public void NothingTicked_SaysSongsRenderOnBlack()
    {
        PackHolds(Entry("amber.mp4"));

        Assert.Contains("plain black",
            Render(new Venue.VenueSettings()).Markup);
    }

    [Fact]
    public void OneTicked_NamesTheBackgroundEverySongGets()
    {
        PackHolds(Entry("amber.mp4"), Entry("violet.mp4"));

        var markup = Render(new Venue.VenueSettings
        {
            SongBackgrounds = ["amber.mp4"],
        }).Markup;

        Assert.Contains("Every song uses amber", markup);
    }

    [Fact]
    public void SeveralTicked_SaysADifferentOneEachSong()
    {
        PackHolds(Entry("amber.mp4"), Entry("violet.mp4"));

        var markup = Render(new Venue.VenueSettings
        {
            SongBackgrounds = ["amber.mp4", "violet.mp4"],
        }).Markup;

        Assert.Contains("different one of these 2", markup);
    }

    /// <summary>A clip with no picture beside it is still pickable, by name.</summary>
    [Fact]
    public void ABackgroundWithNoStill_IsDrawnWithoutOne()
    {
        PackHolds(Entry("amber.mp4", still: false));

        var cut = Render(new Venue.VenueSettings());

        Assert.Empty(cut.FindAll(".kh-background-tile img"));
        Assert.NotNull(cut.Find(".kh-background-tile__art--blank"));
    }

    /// <summary>The browser cannot reach the folder, so a still is fetched by name.</summary>
    [Fact]
    public void ABackgroundWithAStill_FetchesItByNameRatherThanPath()
    {
        PackHolds(Entry("amber.mp4"));

        var src = Render(new Venue.VenueSettings())
            .Find(".kh-background-tile img").GetAttribute("src");

        Assert.Equal("/venue/background-still?file=amber.mp4", src);
        Assert.DoesNotContain("amber.jpg", src);
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
