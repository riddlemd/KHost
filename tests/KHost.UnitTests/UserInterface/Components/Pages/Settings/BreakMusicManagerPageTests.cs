using Bunit;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services.Messaging;
using KHost.Plugins.Sdk.Messaging;
using KHost.Plugins.Sdk.Services;
using KHost.UserInterface.Components.Pages.Settings;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.UserInterface.Components.Pages.Settings;

/// <summary>
/// A venue keeps the source name of the provider it chose, and a plugin that fails to load takes
/// that provider out of the list without touching the venue. The select then holds a value no
/// option carries, which browsers render blank — the state a Spotify plugin with an unparseable
/// manifest left the page in.
/// </summary>
public class BreakMusicManagerPageTests : BunitContext
{
    private const string ModeSelectSelector = "#break-music-mode";
    private const string WarningSelector = ".kh-break-music-manager__hint--warning";

    private readonly IMediaPoolService _mediaPools = Substitute.For<IMediaPoolService>();
    private readonly IBreakMusicService _breakMusic = Substitute.For<IBreakMusicService>();
    private readonly IVenuesService _venues = Substitute.For<IVenuesService>();
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();
    private readonly IFlashService _flash = Substitute.For<IFlashService>();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);

    public BreakMusicManagerPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        _mediaPools.ReadAllWithEntriesAsync(Arg.Any<PoolPurpose>(), Arg.Any<Guid?>())
            .Returns(new List<MediaPool>());

        // Built before the Returns call: NSubstitute rejects a substitute created inside one.
        var library = Provider("Library", nameof(LibraryBreakMusicProviderStub));

        _breakMusic.Providers.Returns(new List<IBreakMusicProvider> { library });
        _breakMusic.ActiveProvider.Returns(library);

        Services.AddSingleton(_mediaPools);
        // The page renders EditPlaylistDialog, which injects these whether or not it is open.
        Services.AddSingleton(Substitute.For<IMediaService>());
        Services.AddSingleton(Substitute.For<IAppSettingsService>());
        Services.AddSingleton(_breakMusic);
        Services.AddSingleton(_venues);
        Services.AddSingleton(_dialogs);
        Services.AddSingleton(_flash);
        Services.AddSingleton<IMessageBroker>(_broker);
    }

    [Fact]
    public void VenuesProviderIsNotLoaded_StillCarriesAnOptionSoTheSelectIsNotBlank()
    {
        ArrangeVenue("SpotifyBreakMusicProvider");

        var options = Render<BreakMusicManagerPage>().FindAll($"{ModeSelectSelector} option");

        Assert.Contains(options, option => option.GetAttribute("value") == "SpotifyBreakMusicProvider");
    }

    [Fact]
    public void VenuesProviderIsNotLoaded_SaysSoRatherThanLookingUnset()
    {
        ArrangeVenue("SpotifyBreakMusicProvider");

        Assert.Single(Render<BreakMusicManagerPage>().FindAll(WarningSelector));
    }

    [Fact]
    public void VenuesProviderIsLoaded_AddsNoExtraOption()
    {
        ArrangeVenue(nameof(LibraryBreakMusicProviderStub));

        var cut = Render<BreakMusicManagerPage>();

        Assert.Single(cut.FindAll($"{ModeSelectSelector} option"));
        Assert.Empty(cut.FindAll(WarningSelector));
    }

    [Fact]
    public void VenueHasNoProviderSet_AddsNoExtraOption()
    {
        ArrangeVenue(null);

        var cut = Render<BreakMusicManagerPage>();

        Assert.Single(cut.FindAll($"{ModeSelectSelector} option"));
        Assert.Empty(cut.FindAll(WarningSelector));
    }

    /// <summary>Source names are a stored key, so they match however they were cased when written.</summary>
    [Fact]
    public void VenuesProviderDiffersOnlyByCase_IsTreatedAsLoaded()
    {
        ArrangeVenue(nameof(LibraryBreakMusicProviderStub).ToUpperInvariant());

        var cut = Render<BreakMusicManagerPage>();

        Assert.Single(cut.FindAll($"{ModeSelectSelector} option"));
        Assert.Empty(cut.FindAll(WarningSelector));
    }

    /// <summary>A cleared setting stores "", which the null-coalescing fallback lets straight
    /// through — the select then carries a value no option has, and renders as blank as a missing
    /// provider does.</summary>
    [Fact]
    public void VenuesProviderIsBlank_FallsBackToTheActiveProviderRatherThanEmptyingTheSelect()
    {
        ArrangeVenue("");

        var cut = Render<BreakMusicManagerPage>();

        Assert.Single(cut.FindAll($"{ModeSelectSelector} option"));
        Assert.Empty(cut.FindAll(WarningSelector));
        Assert.Equal(nameof(LibraryBreakMusicProviderStub), cut.Find(ModeSelectSelector).GetAttribute("value"));
    }

    private void ArrangeVenue(string? providerSource)
    {
        var venue = new Venue { Name = "Test Venue" };

        venue.Settings.BreakMusicProvider = providerSource;

        _venues.ReadSelectedVenueAsync().Returns(venue);
    }

    private static IBreakMusicProvider Provider(string displayName, string sourceName)
    {
        var provider = Substitute.For<IBreakMusicProvider>();

        provider.DisplayName.Returns(displayName);
        provider.SourceName.Returns(sourceName);
        provider.RendersThroughHost.Returns(true);

        return provider;
    }

    /// <summary>Names the built-in provider's source key without referencing KHost.Domain's type.</summary>
    private sealed class LibraryBreakMusicProviderStub;
}
