using Bunit;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services.Messaging;
using KHost.Abstractions.Messaging;
using KHost.UserInterface.Components.Dialogs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using KHost.Domain.Services;
using KHost.Domain.Services.QrCodes;

namespace KHost.UnitTests.UserInterface.Components.Dialogs;

/// <summary>The mode is App Settings' now; a venue picks only its playlist, and only for the mode
/// that plays one.</summary>
public class EditVenueDialogBreakMusicTests : BunitContext
{
    // The dialog carries three pickers; only the break music one answers to the mode above it.
    private const string PlaylistSelector = ".kh-venue-settings__picker--break-music";

    private readonly IBreakMusicService _breakMusic = Substitute.For<IBreakMusicService>();
    private readonly IMediaPoolService _mediaPools = Substitute.For<IMediaPoolService>();
    private readonly IMediaService _media = Substitute.For<IMediaService>();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);

    public EditVenueDialogBreakMusicTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddAppSettings();

        _mediaPools.ReadAllWithEntriesAsync(Arg.Any<PoolPurpose>(), Arg.Any<Guid?>())
            .Returns(new List<MediaPool>());

        // Built before the Returns call: NSubstitute rejects a substitute created inside one.
        var library = Provider("Library", nameof(LibraryBreakMusicProviderStub));

        _breakMusic.Providers.Returns(new List<IBreakMusicProvider> { library });
        _breakMusic.ActiveProvider.Returns(library);
        _breakMusic.LibraryProvider.Returns(library);

        Services.AddSingleton(_breakMusic);
        Services.AddSingleton(Substitute.For<IQrCodePngExporter>());
        Services.AddSingleton(Substitute.For<IMediaUploader>());
        Services.AddSingleton(Substitute.For<IFlashService>());
        Services.AddSingleton(_mediaPools);

        // The dialog reads the visualisation playlists as it opens; none is all these need.
        var visualisations = Substitute.For<IVisualisationPlaylistService>();
        visualisations.ReadAllWithEntriesAsync().Returns(new List<VisualisationPlaylist>());
        Services.AddSingleton(visualisations);
        Services.AddSingleton(_media);
        Services.AddSingleton<IMessageBroker>(_broker);

        // The dialog reads the venue's background folder on open; an empty pack is the
        // shape a venue that has never chosen one has.

        // The dialog lists QR sources from the manifests; none here, but it has to resolve.
        var plugins = Substitute.For<IPluginRegistry>();
        plugins.Plugins.Returns([]);
        Services.AddSingleton(plugins);
    }

    [Fact]
    public void TheSection_OffersNoModeToPick()
        => Assert.Empty(Render().FindAll("#venue-break-music-mode"));

    [Fact]
    public void ActiveModeIsTheHostsPlaylists_OffersAPlaylist()
    {
        var cut = Render();

        Assert.NotEmpty(cut.FindAll(PlaylistSelector));
    }

    /// <summary>A playlist applies to the mode the host's playlists feed, not who renders audio.</summary>
    [Fact]
    public void ActiveModeBringsItsOwnMusic_OffersNoPlaylist()
    {
        var library = Provider("Library", nameof(LibraryBreakMusicProviderStub));
        var jukebox = Provider("Jukebox", "JukeboxProvider");
        _breakMusic.Providers.Returns(new List<IBreakMusicProvider> { library, jukebox });
        _breakMusic.LibraryProvider.Returns(library);
        _breakMusic.ActiveProvider.Returns(jukebox);

        Assert.Empty(Render().FindAll(PlaylistSelector));
    }

    private IRenderedComponent<EditVenueDialog> Render()
        => Render<EditVenueDialog>(ps => ps
            .Add(p => p.IsOpen, true)
            .Add(p => p.Venue, new Venue { Name = "Test Venue" }));

    private static IBreakMusicProvider Provider(string displayName, string sourceName)
    {
        var provider = Substitute.For<IBreakMusicProvider>();

        provider.DisplayName.Returns(displayName);
        provider.SourceName.Returns(sourceName);

        return provider;
    }

    /// <summary>Names the built-in provider's source key without referencing KHost.Domain's type.</summary>
    private sealed class LibraryBreakMusicProviderStub;
}
