using Bunit;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services.Messaging;
using KHost.Domain.Services.QrCodes;
using KHost.UserInterface.Components.Dialogs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.UserInterface.Components.Dialogs;

/// <summary>The venue dialog opens with every titled section folded, and a refused save unfolds
/// the ones holding the reason.</summary>
public class EditVenueDialogSectionsTests : BunitContext
{
    private static readonly string[] SectionTitles =
    [
        "Screen marquee", "Screen QR codes", "Visualisations", "Between singers", "Queue behavior",
        "Guests on their phones", "Tipping", "Require confirmation when removing", "Notes",
    ];

    public EditVenueDialogSectionsTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var mediaPools = Substitute.For<IMediaPoolService>();
        mediaPools.ReadAllWithEntriesAsync(Arg.Any<PoolPurpose>(), Arg.Any<Guid?>()).Returns(new List<MediaPool>());
        var breakMusic = Substitute.For<IBreakMusicService>();
        breakMusic.Providers.Returns(new List<IBreakMusicProvider>());
        var visualisations = Substitute.For<IVisualisationPlaylistService>();
        visualisations.ReadAllWithEntriesAsync().Returns(new List<VisualisationPlaylist>());
        var plugins = Substitute.For<IPluginRegistry>();
        plugins.Plugins.Returns([]);

        Services.AddSingleton(breakMusic);
        Services.AddSingleton(mediaPools);
        Services.AddSingleton(visualisations);
        Services.AddSingleton(Substitute.For<IMediaService>());
        Services.AddSingleton(Substitute.For<IQrCodePngExporter>());
        Services.AddSingleton(Substitute.For<IFlashService>());
        Services.AddSingleton(plugins);
        Services.AddSingleton<IMessageBroker>(new MessageBroker(NullLogger<MessageBroker>.Instance));
    }

    [Fact]
    public void Opening_FoldsEveryTitledSection()
    {
        var sections = Render().FindAll("details.kh-venue-settings__section");

        Assert.Equal(SectionTitles, sections.Select(s => s.QuerySelector("summary")!.TextContent.Trim()));
        Assert.All(sections, section => Assert.False(section.HasAttribute("open"), section.TextContent));
    }

    /// <summary>The fixed control column and one-line notes hang off this modifier, which the
    /// shared row styles of the wizard and the other dialogs do not carry.</summary>
    [Fact]
    public void Opening_LaysTheRowsOutInColumns()
        => Assert.Contains("kh-venue-settings--columns", Render().Find("form.kh-venue-settings").ClassList);

    /// <summary>The name is what a host opens the dialog for, so it is never behind a fold.</summary>
    [Fact]
    public void Opening_LeavesTheNameOutsideAnyFold()
    {
        var name = Render().Find(".kh-venue-settings__section--head input");

        Assert.Null(name.Closest("details"));
    }

    /// <summary>A reason inside a folded section would leave Save looking as if it did nothing.</summary>
    [Fact]
    public void Saving_Refused_UnfoldsTheSectionsHoldingTheReason()
    {
        var cut = Render(new Venue { Name = "" });

        cut.Find(".kh-venue-edit-dialog__save-btn").Click();

        cut.WaitForAssertion(() => JSInterop.VerifyInvoke("openSectionsWithErrors"));
    }

    /// <summary>Enter in a field submits the form itself rather than pressing Save.</summary>
    [Fact]
    public void SubmittingTheForm_Refused_UnfoldsTheSectionsHoldingTheReason()
    {
        var cut = Render(new Venue { Name = "" });

        cut.Find("form").Submit();

        cut.WaitForAssertion(() => JSInterop.VerifyInvoke("openSectionsWithErrors"));
    }

    /// <summary>A refused field must say why under itself, or the unfolded section shows nothing and
    /// Save still looks dead. These had no message until a refused song limit showed the gap.</summary>
    [Theory]
    [InlineData("Queue behavior", "#venue-remote-song-limit")]
    [InlineData("Screen marquee", "#marquee-singer-count")]
    public void Saving_AFieldOutOfRange_SaysWhyInsideItsSection(string section, string field)
    {
        var cut = Render(new Venue
        {
            Name = "The Lounge",
            Settings = { RemoteSongLimit = 150, MarqueeEnabled = true, MarqueeSingerCount = 50 },
        });

        cut.Find(".kh-venue-edit-dialog__save-btn").Click();

        var fold = cut.FindAll("details.kh-venue-settings__section")
            .Single(d => d.QuerySelector("summary")!.TextContent.Trim() == section);
        Assert.NotNull(fold.QuerySelector(field));
        Assert.NotEmpty(fold.QuerySelectorAll(".validation-message"));
    }

    [Fact]
    public void Saving_Accepted_UnfoldsNothing()
    {
        Venue? saved = null;
        var cut = Render(new Venue { Name = "The Lounge" }, venue => saved = venue);

        cut.Find(".kh-venue-edit-dialog__save-btn").Click();

        cut.WaitForAssertion(() => Assert.NotNull(saved));
        JSInterop.VerifyNotInvoke("openSectionsWithErrors");
    }

    private IRenderedComponent<EditVenueDialog> Render(Venue? venue = null, Action<Venue>? onSave = null)
        => Render<EditVenueDialog>(ps =>
        {
            ps.Add(p => p.IsOpen, true)
              .Add(p => p.Venue, venue ?? new Venue { Name = "The Lounge" });

            if (onSave is not null)
                ps.Add(p => p.OnSave, onSave);
        });
}
