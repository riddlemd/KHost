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
    public async Task EnsureRoles_PrefersAScreenThatRendersAudio()
    {
        // A silent screen leading would make the room's audio a follower, and a follower is the
        // thing that gets corrected — which must never happen to what the room hears.
        Connect(
            Screen("Lyrics", sync: true, audio: false),
            Screen("Main", sync: true, audio: true));

        Assert.Equal("Main", await _service.EnsureRolesAsync());
    }

    [Fact]
    public async Task EnsureRoles_KeepsTheIncumbent()
    {
        Connect(Screen("A", sync: true, audio: true), Screen("B", sync: true, audio: true));
        var first = await _service.EnsureRolesAsync();

        // Moving a role mid-song makes every follower re-align on a different reference,
        // which is a visible jump on all of them at once.
        Assert.Equal(first, await _service.EnsureRolesAsync());
    }

    [Fact]
    public async Task EnsureRoles_PrefersASyncableScreenForAudio_SoBothRolesStayTogether()
    {
        // Correcting a screen means seeking it, and seeking the one the room hears is audible.
        // Keeping audio on a screen that can also anchor avoids ever needing to.
        Connect(Screen("Chromecast", sync: false, audio: true), Screen("Local", sync: true, audio: true));

        Assert.Equal("Local", await _service.EnsureRolesAsync());
        Assert.Equal("Local", _service.PrimaryScreenId);
        Assert.False(_service.RolesAreSplit);
    }

    [Fact]
    public async Task EnsureRoles_TakesAnUnsyncableAudioScreen_WhenNothingElseRendersAudio()
    {
        // The Cast device is the only thing the room can hear, so it gets the audio role even
        // though it can never hold the primary one.
        Connect(Screen("Chromecast", sync: false, audio: true), Screen("Lyrics", sync: true, audio: false));

        Assert.Equal("Chromecast", await _service.EnsureRolesAsync());
        Assert.Equal("Lyrics", _service.PrimaryScreenId);
        Assert.True(_service.RolesAreSplit);
    }

    [Fact]
    public async Task EnsureRoles_LeavesThePrimaryVacant_WhenNothingCanSync()
    {
        Connect(Screen("Chromecast", sync: false, audio: true));

        Assert.Equal("Chromecast", await _service.EnsureRolesAsync());
        Assert.Null(_service.PrimaryScreenId);
    }

    [Fact]
    public async Task SetAudioScreen_ToACastDevice_SplitsTheRolesRatherThanRefusing()
    {
        // The whole point of the split: the room's audio can live somewhere that cannot sync.
        Connect(Screen("Chromecast", sync: false, audio: true), Screen("Local", sync: true, audio: true));
        await _service.EnsureRolesAsync();

        Assert.True(await _service.SetAudioScreenAsync("Chromecast"));

        Assert.Equal("Chromecast", _service.AudioScreenId);
        Assert.Equal("Local", _service.PrimaryScreenId);
        Assert.True(_service.RolesAreSplit);

        // And the audio actually moves — the local screen goes quiet.
        Assert.True(_service.IsAudioEnabled("Chromecast"));
        Assert.False(_service.IsAudioEnabled("Local"));
    }

    [Fact]
    public async Task SetAudioScreen_IsRefused_ForAScreenThatRendersNoAudio()
    {
        Connect(Screen("Main", sync: true, audio: true), Screen("Lyrics", sync: true, audio: false));
        await _service.EnsureRolesAsync();

        Assert.False(await _service.SetAudioScreenAsync("Lyrics"));
        Assert.Equal("Main", _service.AudioScreenId);
    }

    [Fact]
    public async Task Primary_FollowsTheAudioScreen_WhenItCanSync()
    {
        Connect(Screen("A", sync: true, audio: true), Screen("B", sync: true, audio: true));
        await _service.EnsureRolesAsync();

        await _service.SetAudioScreenAsync("B");

        // Both roles move together, so the audible screen is never the one being corrected.
        Assert.Equal("B", _service.PrimaryScreenId);
        Assert.False(_service.RolesAreSplit);
    }

    [Fact]
    public async Task LosingTheAudioScreen_PromotesAnother()
    {
        var main = Screen("Main", sync: true, audio: true);
        var spare = Screen("Spare", sync: true, audio: true);
        Connect(main, spare);
        await _service.EnsureRolesAsync();
        Assert.Equal("Main", _service.AudioScreenId);

        Connect(spare);
        Disconnect(main);

        // The room cannot be left silent because one screen went away.
        await WaitForAsync(() => _service.AudioScreenId == "Spare");
        Assert.Equal("Spare", _service.PrimaryScreenId);
    }

    [Fact]
    public async Task ThePromotedScreen_BecomesAudible()
    {
        var main = Screen("Main", sync: true, audio: true);
        var spare = Screen("Spare", sync: true, audio: true);
        Connect(main, spare);
        await _service.EnsureRolesAsync();
        _screenServer.ClearReceivedCalls();

        Connect(spare);
        Disconnect(main);
        await WaitForAsync(() => _service.AudioScreenId == "Spare");

        // Promotion has to reach the screen, not just the host's bookkeeping — it was muted.
        Assert.True(_service.IsAudioEnabled("Spare"));
        await _screenServer.Received().SendCommandAsync("Spare",
            Arg.Is<SetVolumeCommand>(c => c.Volume > 0f));
    }

    [Fact]
    public async Task LosingANonRoleScreen_LeavesTheRolesAlone()
    {
        var main = Screen("Main", sync: true, audio: true);
        var other = Screen("Other", sync: true, audio: true);
        Connect(main, other);
        await _service.EnsureRolesAsync();

        Connect(main);
        Disconnect(other);
        await Task.Delay(100);

        // Moving a role that did not need moving is a visible jump on every follower.
        Assert.Equal("Main", _service.AudioScreenId);
        Assert.Equal("Main", _service.PrimaryScreenId);
    }

    [Fact]
    public async Task LosingTheLastScreen_LeavesBothRolesVacant()
    {
        var main = Screen("Main", sync: true, audio: true);
        Connect(main);
        await _service.EnsureRolesAsync();

        Connect();
        Disconnect(main);

        await WaitForAsync(() => _service.AudioScreenId is null);
        Assert.Null(_service.PrimaryScreenId);
    }

    [Fact]
    public async Task LosingTheAudioScreen_FallsBackToASilentScreenForThePrimary()
    {
        var main = Screen("Main", sync: true, audio: true);
        var lyrics = Screen("Lyrics", sync: true, audio: false);
        Connect(main, lyrics);
        await _service.EnsureRolesAsync();

        Connect(lyrics);
        Disconnect(main);

        // Nothing left renders audio, but the synced screens still need something to follow.
        await WaitForAsync(() => _service.PrimaryScreenId == "Lyrics");
        Assert.Null(_service.AudioScreenId);
    }

    [Fact]
    public async Task AudioScreen_IsTheOnlyAudibleScreen_ByDefault()
    {
        Connect(Screen("Main", sync: true, audio: true), Screen("Lyrics", sync: true, audio: true));
        await _service.EnsureRolesAsync();

        Assert.True(_service.IsAudioEnabled("Main"));

        // Two screens playing the same song into one room fight each other.
        Assert.False(_service.IsAudioEnabled("Lyrics"));
    }

    [Fact]
    public async Task AudioScreen_MutesTheOthersOnTheScreensThemselves()
    {
        Connect(Screen("Main", sync: true, audio: true), Screen("Lyrics", sync: true, audio: true));

        await _service.EnsureRolesAsync();

        await _screenServer.Received().SendCommandAsync("Lyrics",
            Arg.Is<SetVolumeCommand>(c => c.Volume == 0f));
        await _screenServer.Received().SendCommandAsync("Main",
            Arg.Is<SetVolumeCommand>(c => c.Volume > 0f));
    }

    [Fact]
    public async Task SetAudioScreen_MovesTheAudioWithTheRole()
    {
        Connect(Screen("Main", sync: true, audio: true), Screen("Lyrics", sync: true, audio: true));
        await _service.EnsureRolesAsync();
        _screenServer.ClearReceivedCalls();

        await _service.SetAudioScreenAsync("Lyrics");

        Assert.Equal("Lyrics", _service.AudioScreenId);
        Assert.True(_service.IsAudioEnabled("Lyrics"));
        Assert.False(_service.IsAudioEnabled("Main"));

        await _screenServer.Received().SendCommandAsync("Main",
            Arg.Is<SetVolumeCommand>(c => c.Volume == 0f));
    }

    [Fact]
    public async Task SetAudioEnabled_UnmutesASecondScreen_AndKeepsItUnmutedAcrossElections()
    {
        Connect(Screen("Main", sync: true, audio: true), Screen("Overflow", sync: true, audio: true));
        await _service.EnsureRolesAsync();

        await _service.SetAudioEnabledAsync("Overflow", true);

        Assert.True(_service.IsAudioEnabled("Overflow"));
        Assert.True(_service.HasAudioOverride("Overflow"));

        await _service.EnsureRolesAsync();
        Assert.True(_service.IsAudioEnabled("Overflow"));
    }

    [Fact]
    public async Task SetAudioEnabled_CanSilenceEvenTheAudioScreen()
    {
        Connect(Screen("Main", sync: true, audio: true));
        await _service.EnsureRolesAsync();

        await _service.SetAudioEnabledAsync("Main", false);

        Assert.False(_service.IsAudioEnabled("Main"));
        await _screenServer.Received().SendCommandAsync("Main",
            Arg.Is<SetVolumeCommand>(c => c.Volume == 0f));
    }

    [Fact]
    public async Task ClearAudioOverride_ReturnsTheScreenToFollowingTheAudioRole()
    {
        Connect(Screen("Main", sync: true, audio: true), Screen("Overflow", sync: true, audio: true));
        await _service.EnsureRolesAsync();
        await _service.SetAudioEnabledAsync("Overflow", true);

        await _service.ClearAudioOverrideAsync("Overflow");

        Assert.False(_service.HasAudioOverride("Overflow"));
        Assert.False(_service.IsAudioEnabled("Overflow"));
    }

    [Fact]
    public async Task SetAudioScreen_RaisesStateChanged()
    {
        Connect(Screen("Main", sync: true, audio: true), Screen("Lyrics", sync: true, audio: true));
        await _service.EnsureRolesAsync();

        var raised = 0;
        _service.StateChanged += (_, _) => raised++;

        await _service.SetAudioScreenAsync("Lyrics");

        Assert.Equal(1, raised);
    }

    private void Disconnect(IScreenConnection screen)
        => _screenServer.ScreenDisconnected += Raise.EventWith(
            _screenServer, new ScreenConnectionEventArgs { Connection = screen });

    // The disconnect handler runs detached so it cannot deadlock the hub lock.
    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++) await Task.Delay(10);
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
