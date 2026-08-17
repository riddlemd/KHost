using KHost.Abstractions.Services.IPC;
using KHost.Domain.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.Domain.Services;

public class ScreenCoordinationServiceTests : IDisposable
{
    private readonly IScreenServer _screenServer = Substitute.For<IScreenServer>();
    private readonly ScreenCoordinationService _service;

    public ScreenCoordinationServiceTests()
        => _service = new ScreenCoordinationService(NullLogger<ScreenCoordinationService>.Instance, _screenServer);

    public void Dispose() => _service.Dispose();

    [Fact]
    public async Task EnsurePrimary_PrefersAScreenThatRendersAudio()
    {
        // A silent screen leading would make the room's audio a follower, and a follower is the
        // thing that gets corrected — which must never happen to what the room hears.
        Connect(
            Screen("Lyrics", sync: true, audio: false),
            Screen("Main", sync: true, audio: true));

        Assert.Equal("Main", await _service.EnsurePrimaryAsync());
    }

    [Fact]
    public async Task EnsurePrimary_KeepsTheIncumbent()
    {
        Connect(Screen("A", sync: true, audio: true), Screen("B", sync: true, audio: true));
        var first = await _service.EnsurePrimaryAsync();

        // Moving the primary mid-song makes every follower re-align on a different reference,
        // which is a visible jump on all of them at once.
        Assert.Equal(first, await _service.EnsurePrimaryAsync());
    }

    [Fact]
    public async Task EnsurePrimary_IgnoresScreensThatCannotSync()
    {
        // A Cast device renders audio but plays on its own schedule, so it cannot define one.
        Connect(Screen("Chromecast", sync: false, audio: true), Screen("Local", sync: true, audio: true));

        Assert.Equal("Local", await _service.EnsurePrimaryAsync());
    }

    [Fact]
    public async Task EnsurePrimary_IsNull_WhenNothingCanSync()
    {
        Connect(Screen("Chromecast", sync: false, audio: true));

        Assert.Null(await _service.EnsurePrimaryAsync());
    }

    [Fact]
    public async Task Primary_IsTheOnlyAudibleScreen_ByDefault()
    {
        Connect(Screen("Main", sync: true, audio: true), Screen("Lyrics", sync: true, audio: true));
        await _service.EnsurePrimaryAsync();

        Assert.True(_service.IsAudioEnabled("Main"));

        // Two screens playing the same song into one room fight each other.
        Assert.False(_service.IsAudioEnabled("Lyrics"));
    }

    [Fact]
    public async Task Primary_MutesTheOthersOnTheScreensThemselves()
    {
        Connect(Screen("Main", sync: true, audio: true), Screen("Lyrics", sync: true, audio: true));

        await _service.EnsurePrimaryAsync();

        await _screenServer.Received().SendCommandAsync("Lyrics",
            Arg.Is<SetVolumeCommand>(c => c.Volume == 0f));
        await _screenServer.Received().SendCommandAsync("Main",
            Arg.Is<SetVolumeCommand>(c => c.Volume > 0f));
    }

    [Fact]
    public async Task SetPrimary_MovesTheAudioWithTheRole()
    {
        Connect(Screen("Main", sync: true, audio: true), Screen("Lyrics", sync: true, audio: true));
        await _service.EnsurePrimaryAsync();
        _screenServer.ClearReceivedCalls();

        await _service.SetPrimaryAsync("Lyrics");

        Assert.Equal("Lyrics", _service.PrimaryScreenId);
        Assert.True(_service.IsAudioEnabled("Lyrics"));
        Assert.False(_service.IsAudioEnabled("Main"));

        await _screenServer.Received().SendCommandAsync("Main",
            Arg.Is<SetVolumeCommand>(c => c.Volume == 0f));
    }

    [Fact]
    public async Task SetPrimary_IsRefused_ForAScreenThatCannotSync()
    {
        Connect(Screen("Chromecast", sync: false, audio: true), Screen("Local", sync: true, audio: true));
        await _service.EnsurePrimaryAsync();

        // It would be defining a timeline it cannot itself be held to.
        Assert.False(await _service.SetPrimaryAsync("Chromecast"));
        Assert.Equal("Local", _service.PrimaryScreenId);
    }

    [Fact]
    public async Task SetAudioEnabled_UnmutesASecondScreen_AndKeepsItUnmutedAcrossElections()
    {
        Connect(Screen("Main", sync: true, audio: true), Screen("Overflow", sync: true, audio: true));
        await _service.EnsurePrimaryAsync();

        await _service.SetAudioEnabledAsync("Overflow", true);

        Assert.True(_service.IsAudioEnabled("Overflow"));
        Assert.True(_service.HasAudioOverride("Overflow"));

        await _service.EnsurePrimaryAsync();
        Assert.True(_service.IsAudioEnabled("Overflow"));
    }

    [Fact]
    public async Task SetAudioEnabled_CanSilenceEvenThePrimary()
    {
        Connect(Screen("Main", sync: true, audio: true));
        await _service.EnsurePrimaryAsync();

        await _service.SetAudioEnabledAsync("Main", false);

        Assert.False(_service.IsAudioEnabled("Main"));
        await _screenServer.Received().SendCommandAsync("Main",
            Arg.Is<SetVolumeCommand>(c => c.Volume == 0f));
    }

    [Fact]
    public async Task ClearAudioOverride_ReturnsTheScreenToFollowingThePrimary()
    {
        Connect(Screen("Main", sync: true, audio: true), Screen("Overflow", sync: true, audio: true));
        await _service.EnsurePrimaryAsync();
        await _service.SetAudioEnabledAsync("Overflow", true);

        await _service.ClearAudioOverrideAsync("Overflow");

        Assert.False(_service.HasAudioOverride("Overflow"));
        Assert.False(_service.IsAudioEnabled("Overflow"));
    }

    [Fact]
    public async Task SetPrimary_RaisesStateChanged()
    {
        Connect(Screen("Main", sync: true, audio: true), Screen("Lyrics", sync: true, audio: true));
        await _service.EnsurePrimaryAsync();

        var raised = 0;
        _service.StateChanged += (_, _) => raised++;

        await _service.SetPrimaryAsync("Lyrics");

        Assert.Equal(1, raised);
    }

    private static IScreenConnection Screen(string id, bool sync, bool audio)
    {
        var screen = Substitute.For<IScreenConnection>();
        screen.ScreenId.Returns(id);
        screen.ConnectionId.Returns($"conn-{id}");
        screen.IsConnected.Returns(true);
        screen.Capabilities.Returns(new ScreenCapabilities
        {
            SupportsSync = sync,
            SupportsAudio = audio,
            SupportsVideo = true,
        });

        return screen;
    }

    private void Connect(params IScreenConnection[] screens)
        => _screenServer.GetConnectedScreensAsync().Returns(_ => ToAsyncEnumerable(screens));

    private static async IAsyncEnumerable<IScreenConnection> ToAsyncEnumerable(IScreenConnection[] screens)
    {
        foreach (var screen in screens) yield return screen;
        await Task.CompletedTask;
    }
}
