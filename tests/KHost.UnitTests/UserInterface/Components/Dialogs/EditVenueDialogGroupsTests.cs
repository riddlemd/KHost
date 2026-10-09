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

/// <summary>The long venue sections, split into groups under their own headings.</summary>
public class EditVenueDialogGroupsTests : BunitContext
{

    public EditVenueDialogGroupsTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddAppSettings();

        var mediaPools = Substitute.For<IMediaPoolService>();
        mediaPools.ReadAllWithEntriesAsync(Arg.Any<PoolPurpose>(), Arg.Any<Guid?>()).Returns(new List<MediaPool>());
        var breakMusic = Substitute.For<IBreakMusicService>();
        breakMusic.Providers.Returns(new List<IBreakMusicProvider>());
        var visualisations = Substitute.For<IVisualisationPlaylistService>();
        visualisations.ReadAllWithEntriesAsync().Returns(new List<VisualisationPlaylist>());
        var plugins = Substitute.For<IPluginRegistry>();
        plugins.Plugins.Returns([]);

        Services.AddSingleton(breakMusic);
        Services.AddSingleton(Substitute.For<IQrCodePngExporter>());
        Services.AddSingleton(Substitute.For<IMediaUploader>());
        Services.AddSingleton(Substitute.For<IFlashService>());
        Services.AddSingleton(mediaPools);
        Services.AddSingleton(visualisations);
        Services.AddSingleton(Substitute.For<IMediaService>());
        Services.AddSingleton<IMessageBroker>(new MessageBroker(NullLogger<MessageBroker>.Instance));
        Services.AddSingleton(plugins);
    }

    public static TheoryData<string, string> MarqueeRows => new()
    {
        { "#marquee-message", "Content" },
        { "#marquee-singer-count", "Content" },
        { "#marquee-position", "Placement" },
        { "#venue-marquee-hide-during-song", "Placement" },
        { "#marquee-font-size", "Look" },
        { "#marquee-divider-shape", "Look" },
    };

    [Theory, MemberData(nameof(MarqueeRows))]
    public void TheMarquee_EachRow_SitsInItsGroup(string row, string group)
        => Assert.Equal(group, GroupOf(Render(new Venue.VenueSettings { MarqueeEnabled = true }), row));

    public static TheoryData<string> MarqueeColourRows =>
        ["#marquee-background", "#marquee-background-opacity", "#marquee-text", "#marquee-singer-color", "#marquee-song-color", "#marquee-divider-color"];

    /// <summary>Every colour lives with the screen's others, the marquee's included, and can be set
    /// before the marquee is turned on.</summary>
    [Theory, MemberData(nameof(MarqueeColourRows))]
    public void TheMarqueesColours_SitInScreenColoursEvenWithTheMarqueeOff(string row)
    {
        var cut = Render(new Venue.VenueSettings { MarqueeEnabled = false });

        Assert.Equal(("Marquee", "Screen colours"), (GroupOf(cut, row), SectionOf(cut, row)));
    }

    [Fact]
    public void TheMarquee_TurningItOn_StaysAboveEveryGroup()
        => Assert.Null(Render(new Venue.VenueSettings { MarqueeEnabled = true }).Find("#venue-marquee-enabled").Closest(".kh-venue-settings-group"));

    public static TheoryData<string, string> QrCodeRows => new()
    {
        { "#venue-qr-corner", "Placement" },
        { "#venue-qr-offset", "Placement" },
        { "#venue-qr-hide-during-song", "Placement" },
        { "#venue-qr-size", "Look" },
        { "#venue-qr-safe-zone", "Look" },
    };

    [Theory, MemberData(nameof(QrCodeRows))]
    public void TheQrCode_EachRow_SitsInItsGroup(string row, string group)
        => Assert.Equal(group, GroupOf(Render(new Venue.VenueSettings { QrCodeSource = "remote" }), row));

    public static TheoryData<string, string> QueueRows => new()
    {
        { "#venue-remote-song-limit", "Limits" },
        { "#venue-refuse-song-queued-for-another-singer", "Limits" },
        { "#duplicate-window", "Duplicate songs" },
        { "#venue-allow-aliases", "Showing the queue" },
    };

    [Theory, MemberData(nameof(QueueRows))]
    public void TheQueue_EachRow_SitsInItsGroup(string row, string group)
        => Assert.Equal(group, GroupOf(Render(new Venue.VenueSettings { WarnOnDuplicateSong = true }), row));

    private static string? GroupOf(IRenderedComponent<EditVenueDialog> cut, string row)
        => cut.Find(row).Closest(".kh-venue-settings-group")?.QuerySelector(".kh-venue-settings-group__title")?.TextContent;

    private static string? SectionOf(IRenderedComponent<EditVenueDialog> cut, string row)
        => cut.Find(row).Closest("details")?.QuerySelector("summary")?.TextContent.Trim();

    private IRenderedComponent<EditVenueDialog> Render(Venue.VenueSettings settings)
        => Render<EditVenueDialog>(ps => ps
            .Add(p => p.IsOpen, true)
            .Add(p => p.Venue, new Venue { Name = "Test Venue", Settings = settings }));
}
