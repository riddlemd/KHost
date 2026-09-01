using Bunit;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services.Messaging;
using KHost.Plugins.Sdk.Messaging;
using KHost.Plugins.Sdk.Services;
using KHost.UserInterface.Components.Dialogs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.UserInterface.Components.Dialogs;

/// <summary>
/// A venue keeps the source name of the mode it chose, and a plugin that fails to load takes that
/// provider out of the list without touching the venue. The select then holds a value no option
/// carries, which browsers render blank — the state a Spotify plugin with an unparseable manifest
/// left the page in. The selector lives on the venue now, which is where the setting always was.
/// </summary>
public class EditVenueDialogBreakMusicTests : BunitContext
{
    private const string ModeSelectSelector = "#venue-break-music-mode";
    private const string WarningSelector = ".kh-venue-settings__hint--warning";
    // The dialog carries three pickers; only the break music one answers to the mode above it.
    private const string PlaylistSelector = ".kh-venue-settings__picker--break-music";

    private readonly IBreakMusicService _breakMusic = Substitute.For<IBreakMusicService>();
    private readonly IMediaPoolService _mediaPools = Substitute.For<IMediaPoolService>();
    private readonly IMediaService _media = Substitute.For<IMediaService>();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);

    public EditVenueDialogBreakMusicTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        _mediaPools.ReadAllWithEntriesAsync(Arg.Any<PoolPurpose>(), Arg.Any<Guid?>())
            .Returns(new List<MediaPool>());

        // Built before the Returns call: NSubstitute rejects a substitute created inside one.
        var library = Provider("Library", nameof(LibraryBreakMusicProviderStub), rendersThroughHost: true);

        _breakMusic.Providers.Returns(new List<IBreakMusicProvider> { library });
        _breakMusic.ActiveProvider.Returns(library);
        _breakMusic.LibraryProvider.Returns(library);

        Services.AddSingleton(_breakMusic);
        Services.AddSingleton(_mediaPools);
        Services.AddSingleton(_media);
        Services.AddSingleton<IMessageBroker>(_broker);
    }

    [Fact]
    public void VenuesModeIsNotLoaded_StillCarriesAnOptionSoTheSelectIsNotBlank()
    {
        var options = Render("SpotifyBreakMusicProvider").FindAll($"{ModeSelectSelector} option");

        Assert.Contains(options, option => option.GetAttribute("value") == "SpotifyBreakMusicProvider");
    }

    [Fact]
    public void VenuesModeIsNotLoaded_SaysSoRatherThanLookingUnset()
        => Assert.Single(Render("SpotifyBreakMusicProvider").FindAll(WarningSelector));

    [Fact]
    public void VenuesModeIsLoaded_AddsNoExtraOption()
    {
        var cut = Render(nameof(LibraryBreakMusicProviderStub));

        Assert.Single(cut.FindAll($"{ModeSelectSelector} option"));
        Assert.Empty(cut.FindAll(WarningSelector));
    }

    [Fact]
    public void VenueHasNoModeSet_FallsBackToTheRunningOne()
    {
        var cut = Render(null);

        Assert.Single(cut.FindAll($"{ModeSelectSelector} option"));
        Assert.Empty(cut.FindAll(WarningSelector));
        Assert.Equal(nameof(LibraryBreakMusicProviderStub), cut.Find(ModeSelectSelector).GetAttribute("value"));
    }

    /// <summary>Source names are a stored key, so they match however they were cased when written.</summary>
    [Fact]
    public void VenuesModeDiffersOnlyByCase_IsTreatedAsLoaded()
    {
        var cut = Render(nameof(LibraryBreakMusicProviderStub).ToUpperInvariant());

        Assert.Single(cut.FindAll($"{ModeSelectSelector} option"));
        Assert.Empty(cut.FindAll(WarningSelector));
    }

    /// <summary>A cleared setting stores "", which no option carries either.</summary>
    [Fact]
    public void VenuesModeIsBlank_FallsBackToTheRunningOneRatherThanEmptyingTheSelect()
    {
        var cut = Render("");

        Assert.Single(cut.FindAll($"{ModeSelectSelector} option"));
        Assert.Empty(cut.FindAll(WarningSelector));
        Assert.Equal(nameof(LibraryBreakMusicProviderStub), cut.Find(ModeSelectSelector).GetAttribute("value"));
    }

    [Fact]
    public void ModeIsTheLibraryOne_OffersAPlaylist()
        => Assert.NotEmpty(Render(nameof(LibraryBreakMusicProviderStub)).FindAll(PlaylistSelector));

    /// <summary>
    /// Deliberately a provider that renders through the host: a playlist applies to the mode this
    /// host's playlists feed, which is not the same question as who plays the audio.
    /// </summary>
    [Fact]
    public void ModeBringsItsOwnMusic_OffersNoPlaylistEvenWhenTheHostRendersIt()
    {
        var library = Provider("Library", nameof(LibraryBreakMusicProviderStub), rendersThroughHost: true);
        var other = Provider("Jukebox", "JukeboxProvider", rendersThroughHost: true);

        _breakMusic.Providers.Returns(new List<IBreakMusicProvider> { library, other });
        _breakMusic.LibraryProvider.Returns(library);

        Assert.Empty(Render("JukeboxProvider").FindAll(PlaylistSelector));
    }

    private IRenderedComponent<EditVenueDialog> Render(string? providerSource)
    {
        var venue = new Venue { Name = "Test Venue" };
        venue.Settings.BreakMusicProvider = providerSource;

        return Render<EditVenueDialog>(ps => ps
            .Add(p => p.IsOpen, true)
            .Add(p => p.Venue, venue));
    }

    private static IBreakMusicProvider Provider(string displayName, string sourceName, bool rendersThroughHost)
    {
        var provider = Substitute.For<IBreakMusicProvider>();

        provider.DisplayName.Returns(displayName);
        provider.SourceName.Returns(sourceName);
        provider.RendersThroughHost.Returns(rendersThroughHost);

        return provider;
    }

    /// <summary>Names the built-in provider's source key without referencing KHost.Domain's type.</summary>
    private sealed class LibraryBreakMusicProviderStub;
}
