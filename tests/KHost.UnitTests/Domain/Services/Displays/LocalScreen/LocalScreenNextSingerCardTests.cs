using KHost.Abstractions.Messaging.Messages;
using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.Abstractions.Services;
using KHost.Domain.Services;
using KHost.Domain.Services.Displays.LocalScreen;
using KHost.Domain.Services.Messaging;
using KHost.Domain.Services.Visualisations;
using KHost.IPC.SignalR.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace KHost.UnitTests.Domain.Services.Displays.LocalScreen;

/// <summary>What the "Up next" card is drawn over: the venue's picture unless it chose otherwise,
/// and a visualisation only when the venue's playlist has one to give.</summary>
public class LocalScreenNextSingerCardTests
{
    private static readonly Guid PlaylistId = Guid.NewGuid();

    private readonly IScreenServer _screenServer = Substitute.For<IScreenServer>();
    private readonly MessageBroker _broker = new(NullLogger<MessageBroker>.Instance);
    private readonly Venue.VenueSettings _settings = new() { VisualisationPlaylistId = PlaylistId };
    private readonly IVisualisationPlaylistRepository _playlists = Substitute.For<IVisualisationPlaylistRepository>();
    private readonly IVisualiserPresetService _presets = Substitute.For<IVisualiserPresetService>();
    private readonly IOptionsMonitor<HlsMediaStreamService.ServiceOptions> _streamOptions = Substitute.For<IOptionsMonitor<HlsMediaStreamService.ServiceOptions>>();
    private readonly ISongLevelsService _levels = Substitute.For<ISongLevelsService>();
    private readonly IVenuesService _venues = Substitute.For<IVenuesService>();
    private readonly IPlaybackService _playback = Substitute.For<IPlaybackService>();
    private readonly VisualisationPlaylist _playlist = new()
    {
        Id = PlaylistId,
        Name = "Night",
        Entries =
        [
            new() { PresetName = "Rovastar - Oozing Resistance", Brightness = 80, Saturation = 150, Sensitivity = 200 },
            new() { PresetName = "_Mig_049" },
        ],
    };

    public LocalScreenNextSingerCardTests()
    {
        _venues.ReadSelectedVenueAsync().Returns(new Venue { Name = "The Bar", Settings = _settings });
        _playback.CurrentProgram.Returns(new PlaybackProgram.Idle());
        _playlists.ReadWithEntriesAsync(PlaylistId).Returns(_ => _playlist);
        _playlists.ReadAsync(PlaylistId).Returns(_ => _playlist);
        _presets.ReadAll().Returns([]);
        _streamOptions.CurrentValue.Returns(new HlsMediaStreamService.ServiceOptions { BaseAddress = "http://host:5251/" });
    }

    private LocalScreenDisplayProvider Provider()
        => new(
            NullLogger<LocalScreenDisplayProvider>.Instance, _screenServer, [], _broker, _venues,
            services: new ServiceCollection()
                .AddSingleton(_playback)
                .AddSingleton(_levels)
                .AddSingleton<IVisualisationPlaylistService>(new VisualisationPlaylistService(
                    NullLogger<VisualisationPlaylistService>.Instance, _playlists, _broker, new Random(1)))
                .AddSingleton(_presets)
                .AddSingleton(_streamOptions)
                .BuildServiceProvider(),
            redrawSettle: TimeSpan.Zero);

    private Task AnnounceAsync()
        => _broker.PublishAsync(new NextSingerAnnounced(new NextSingerCard { Singer = "Ada", Song = "Today" }));

    private List<ShowNextSingerCommand> Cards()
        => [.. _screenServer.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IScreenServer.BroadcastCommandAsync))
            .Select(call => call.GetArguments()[0])
            .OfType<ShowNextSingerCommand>()];

    [Fact]
    public async Task NextSingerAnnounced_VenueNeverChose_DrawsOverTheVenuesPicture()
    {
        using var provider = Provider();

        await AnnounceAsync();

        var card = Assert.Single(Cards());
        Assert.Equal(NextSingerBackground.Over, card.Background);
        Assert.Null(card.Visualiser);
    }

    [Fact]
    public async Task NextSingerAnnounced_Blackout_DrawsOnBlack()
    {
        _settings.NextSingerBackground = NextSingerBackground.Blackout;
        using var provider = Provider();

        await AnnounceAsync();

        var card = Assert.Single(Cards());
        Assert.Equal(NextSingerBackground.Blackout, card.Background);
        Assert.Null(card.Visualiser);
    }

    [Fact]
    public async Task NextSingerAnnounced_Visualisation_SendsThePlaylistsNextEntryWithNoLevels()
    {
        _settings.NextSingerBackground = NextSingerBackground.Visualisation;
        using var provider = Provider();

        await AnnounceAsync();

        var card = Assert.Single(Cards());
        Assert.Equal(NextSingerBackground.Visualisation, card.Background);
        Assert.NotNull(card.Visualiser);
        Assert.True(card.Visualiser!.Enabled);
        Assert.Equal("Rovastar - Oozing Resistance", card.Visualiser.PresetName);
        Assert.Equal((80, 150, 200), (card.Visualiser.Brightness, card.Visualiser.Saturation, card.Visualiser.Sensitivity));
        Assert.Null(card.Visualiser.LevelsUrl);
        _levels.DidNotReceive().Begin(Arg.Any<IReadOnlyList<SongLevelsInput>>());
    }

    /// <summary>The card takes a turn in the rotation like a song, so a second card moves on.</summary>
    [Fact]
    public async Task NextSingerAnnounced_Visualisation_TakesTheNextTurnInTheRotation()
    {
        _settings.NextSingerBackground = NextSingerBackground.Visualisation;
        using var provider = Provider();

        await AnnounceAsync();
        await AnnounceAsync();

        Assert.Equal(["Rovastar - Oozing Resistance", "_Mig_049"], Cards().Select(card => card.Visualiser!.PresetName));
    }

    [Fact]
    public async Task NextSingerAnnounced_VisualisationWithNoPlaylist_FallsBackToOver()
    {
        _settings.NextSingerBackground = NextSingerBackground.Visualisation;
        _settings.VisualisationPlaylistId = null;
        using var provider = Provider();

        await AnnounceAsync();

        var card = Assert.Single(Cards());
        Assert.Equal(NextSingerBackground.Over, card.Background);
        Assert.Null(card.Visualiser);
    }

    [Fact]
    public async Task NextSingerAnnounced_VisualisationWithAnEmptyPlaylist_FallsBackToOver()
    {
        _settings.NextSingerBackground = NextSingerBackground.Visualisation;
        _playlist.Entries.Clear();
        using var provider = Provider();

        await AnnounceAsync();

        var card = Assert.Single(Cards());
        Assert.Equal(NextSingerBackground.Over, card.Background);
        Assert.Null(card.Visualiser);
    }

    [Fact]
    public async Task NextSingerAnnounced_VisualisationWhosePresetIsGone_FallsBackToOver()
    {
        _settings.NextSingerBackground = NextSingerBackground.Visualisation;
        foreach (var entry in _playlist.Entries)
        {
            entry.PresetSource = VisualiserPresetSource.BuiltIn;
            entry.PresetName = "no-such-drawing";
        }
        using var provider = Provider();

        await AnnounceAsync();

        var card = Assert.Single(Cards());
        Assert.Equal(NextSingerBackground.Over, card.Background);
        Assert.Null(card.Visualiser);
    }
}
