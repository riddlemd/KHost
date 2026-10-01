using Bunit;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services.Messaging;
using KHost.UserInterface.Components.Dialogs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.UserInterface.Components.Dialogs;

/// <summary>What the "Up next" card is drawn over, chosen under Between singers.</summary>
public class EditVenueDialogNextSingerTests : BunitContext
{
    private const string BackgroundSelector = "#venue-next-singer-background";

    private readonly IBreakMusicService _breakMusic = Substitute.For<IBreakMusicService>();
    private readonly IMediaPoolService _mediaPools = Substitute.For<IMediaPoolService>();
    private readonly IMediaService _media = Substitute.For<IMediaService>();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);

    public EditVenueDialogNextSingerTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        _mediaPools.ReadAllWithEntriesAsync(Arg.Any<PoolPurpose>(), Arg.Any<Guid?>())
            .Returns(new List<MediaPool>());
        _breakMusic.Providers.Returns(new List<IBreakMusicProvider>());

        Services.AddSingleton(_breakMusic);
        Services.AddSingleton(_mediaPools);
        Services.AddSingleton(_media);
        Services.AddSingleton<IMessageBroker>(_broker);

        var visualisations = Substitute.For<IVisualisationPlaylistService>();
        visualisations.ReadAllWithEntriesAsync().Returns(new List<VisualisationPlaylist>());
        Services.AddSingleton(visualisations);

        var plugins = Substitute.For<IPluginRegistry>();
        plugins.Plugins.Returns([]);
        Services.AddSingleton(plugins);
    }

    [Fact]
    public void AVenueThatNeverChose_OpensOnThePlaceholderImage()
    {
        var cut = Render(new Venue.VenueSettings());

        Assert.Equal(nameof(NextSingerBackground.Over), cut.Find(BackgroundSelector).GetAttribute("value"));
    }

    [Fact]
    public void OffersEachBackgroundUnderItsOwnName()
    {
        var cut = Render(new Venue.VenueSettings());

        var offered = cut.FindAll($"{BackgroundSelector} option").Select(option => (option.GetAttribute("value"), option.TextContent.Trim()));

        Assert.Equal(
        [
            (nameof(NextSingerBackground.Over), "Placeholder image"),
            (nameof(NextSingerBackground.Blackout), "Black"),
            (nameof(NextSingerBackground.Visualisation), "A visualisation"),
        ], offered);
    }

    [Theory]
    [InlineData(NextSingerBackground.Blackout)]
    [InlineData(NextSingerBackground.Visualisation)]
    public void PickingABackground_ReachesTheSavedVenue(NextSingerBackground picked)
    {
        Venue? saved = null;
        var cut = Render(new Venue.VenueSettings(), venue => saved = venue);

        cut.Find(BackgroundSelector).Change(picked.ToString());
        cut.Find("form").Submit();

        Assert.NotNull(saved);
        Assert.Equal(picked, saved!.Settings.NextSingerBackground);
    }

    [Fact]
    public void PickingThePlaceholderImageBack_ReachesTheSavedVenue()
    {
        Venue? saved = null;
        var cut = Render(new Venue.VenueSettings { NextSingerBackground = NextSingerBackground.Blackout }, venue => saved = venue);

        Assert.Equal(nameof(NextSingerBackground.Blackout), cut.Find(BackgroundSelector).GetAttribute("value"));
        cut.Find(BackgroundSelector).Change(nameof(NextSingerBackground.Over));
        cut.Find("form").Submit();

        Assert.NotNull(saved);
        Assert.Equal(NextSingerBackground.Over, saved!.Settings.NextSingerBackground);
    }

    private IRenderedComponent<EditVenueDialog> Render(Venue.VenueSettings settings, Action<Venue>? onSave = null)
        => Render<EditVenueDialog>(ps => ps
            .Add(p => p.IsOpen, true)
            .Add(p => p.Venue, new Venue { Name = "Test Venue", Settings = settings })
            .Add(p => p.OnSave, venue => onSave?.Invoke(venue)));
}
