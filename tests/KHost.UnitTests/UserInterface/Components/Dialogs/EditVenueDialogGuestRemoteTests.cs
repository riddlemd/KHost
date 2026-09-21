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

/// <summary>Whether guests may join from their phones at all, and whether those who do see the
/// queue. Both default on, so a venue that has never been asked must offer them on.</summary>
public class EditVenueDialogGuestRemoteTests : BunitContext
{
    private const string RemoteSelector = "#venue-allow-guest-remote";
    private const string QueueSelector = "#venue-show-queue-to-guests";

    private readonly IBreakMusicService _breakMusic = Substitute.For<IBreakMusicService>();
    private readonly IMediaPoolService _mediaPools = Substitute.For<IMediaPoolService>();
    private readonly IMediaService _media = Substitute.For<IMediaService>();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);

    public EditVenueDialogGuestRemoteTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        _mediaPools.ReadAllWithEntriesAsync(Arg.Any<PoolPurpose>(), Arg.Any<Guid?>())
            .Returns(new List<MediaPool>());
        _breakMusic.Providers.Returns(new List<IBreakMusicProvider>());

        Services.AddSingleton(_breakMusic);
        Services.AddSingleton(_mediaPools);
        Services.AddSingleton(_media);
        Services.AddSingleton<IMessageBroker>(_broker);

        // The dialog reads the venue's background folder on open; an empty pack is the
        // shape a venue that has never chosen one has.
        var backgroundPacks = Substitute.For<IBackgroundPackService>();
        backgroundPacks.ReadAsync(Arg.Any<CancellationToken>())
            .Returns(new BackgroundPack());
        Services.AddSingleton(backgroundPacks);

        var plugins = Substitute.For<IPluginRegistry>();
        plugins.Plugins.Returns([]);
        Services.AddSingleton(plugins);
    }

    /// <summary>The backfill exists so a stored venue reads true, and a venue object built in
    /// memory carries the initializer. Both switches have to show that rather than the bool's
    /// own default, or a host opening the dialog would see the feature off and save it off.
    /// </summary>
    [Fact]
    public void AVenueNeverAsked_OffersBothSwitchesOn()
    {
        var cut = Render(new Venue.VenueSettings());

        Assert.True(cut.Find(RemoteSelector).HasAttribute("checked"));
        Assert.True(cut.Find(QueueSelector).HasAttribute("checked"));
    }

    [Fact]
    public void AVenueThatClosedTheRoom_ShowsTheSwitchOff()
        => Assert.False(Render(new Venue.VenueSettings { AllowGuestRemote = false })
            .Find(RemoteSelector).HasAttribute("checked"));

    /// <summary>Showing the queue means nothing with no guests to show it to, so the dialog does
    /// not offer a switch that decides nothing.</summary>
    [Fact]
    public void WithGuestsTurnedAway_TheQueueSwitchIsNotOffered()
    {
        var cut = Render(new Venue.VenueSettings { AllowGuestRemote = false });

        Assert.Empty(cut.FindAll(QueueSelector));
    }

    [Fact]
    public void ClosingTheRoom_ReachesTheVenueThatIsSaved()
    {
        Venue? saved = null;
        var cut = Render(new Venue.VenueSettings(), venue => saved = venue);

        cut.Find(RemoteSelector).Change(false);
        cut.Find("form").Submit();

        Assert.NotNull(saved);
        Assert.False(saved!.Settings.AllowGuestRemote);
    }

    /// <summary>Read into the model but never saved back, this would look right and never take.</summary>
    [Fact]
    public void HidingTheQueue_ReachesTheVenueThatIsSaved()
    {
        Venue? saved = null;
        var cut = Render(new Venue.VenueSettings(), venue => saved = venue);

        cut.Find(QueueSelector).Change(false);
        cut.Find("form").Submit();

        Assert.NotNull(saved);
        Assert.False(saved!.Settings.ShowQueueToGuests);
        Assert.True(saved!.Settings.AllowGuestRemote);
    }

    /// <summary>Turning the room back on must not silently carry a hidden queue with it.</summary>
    [Fact]
    public void ReopeningTheRoom_LeavesTheQueueAnswerAsItWas()
    {
        Venue? saved = null;
        var cut = Render(
            new Venue.VenueSettings { AllowGuestRemote = false, ShowQueueToGuests = false },
            venue => saved = venue);

        cut.Find(RemoteSelector).Change(true);
        cut.Find("form").Submit();

        Assert.NotNull(saved);
        Assert.True(saved!.Settings.AllowGuestRemote);
        Assert.False(saved!.Settings.ShowQueueToGuests);
    }

    /// <summary>Closing the room is not obvious: a guest who kept the link needs no code, so a
    /// host taking the QR code down has not closed anything.</summary>
    [Fact]
    public void TheRoomSwitch_ExplainsWhatItChanges()
    {
        var cut = Render(new Venue.VenueSettings());

        // Scoped to this switch's own row: the dialog is full of notes, and another control's
        // would pass this while saying nothing about guests.
        var note = cut.Find($".kh-form-check:has({RemoteSelector}) .kh-note");

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
