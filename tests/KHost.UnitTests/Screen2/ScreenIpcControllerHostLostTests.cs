using KHost.Abstractions.Services.IPC;
using KHost.Screen2;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace KHost.UnitTests.Screen2;

/// <summary>Losing the host is the one failure the room sees, so it gets its own tests.</summary>
public class ScreenIpcControllerHostLostTests : IAsyncDisposable
{
    private readonly IScreenClient _client = Substitute.For<IScreenClient>();
    private readonly StreamMediaPlayer _player = new(NullLogger<StreamMediaPlayer>.Instance);
    private readonly List<string> _sentToPage = [];
    private readonly ScreenIpcController _controller;

    public ScreenIpcControllerHostLostTests()
    {
        _player.SendToBrowser = _sentToPage.Add;
        _controller = new ScreenIpcController(_client, _player, NullLogger<ScreenIpcController>.Instance);
    }

    [Fact]
    public void LosingTheConnectionOutright_PausesAndSaysSo()
    {
        Transition(ScreenClientState.Connected, ScreenClientState.Disconnected);

        // The host is the only stop button this screen has.
        Assert.Contains(_sentToPage, m => m.Contains("\"pause\""));
        Assert.Contains(_sentToPage, m => m.Contains("hostLost") && m.Contains("true"));
    }

    [Fact]
    public void AnErroredConnection_IsTreatedAsLost()
    {
        Transition(ScreenClientState.Connected, ScreenClientState.Error);

        Assert.Contains(_sentToPage, m => m.Contains("hostLost") && m.Contains("true"));
    }

    [Fact]
    public void AReconnect_DoesNotBlankTheScreenImmediately()
    {
        Transition(ScreenClientState.Connected, ScreenClientState.Reconnecting);

        // Automatic reconnect recovers in seconds; blanking on every blip is worse than waiting.
        Assert.DoesNotContain(_sentToPage, m => m.Contains("hostLost"));
        Assert.DoesNotContain(_sentToPage, m => m.Contains("\"pause\""));
    }

    [Fact]
    public void ComingBack_ClearsTheNoticeWithoutHavingShownIt()
    {
        Transition(ScreenClientState.Connected, ScreenClientState.Reconnecting);
        Transition(ScreenClientState.Reconnecting, ScreenClientState.Connected);

        // Never lost, so there is nothing to say — the screen should not flash a notice.
        Assert.Empty(_sentToPage);
    }

    [Fact]
    public void ComingBackAfterALoss_ClearsTheNotice()
    {
        Transition(ScreenClientState.Connected, ScreenClientState.Disconnected);
        _sentToPage.Clear();

        Transition(ScreenClientState.Disconnected, ScreenClientState.Connected);

        Assert.Contains(_sentToPage, m => m.Contains("hostLost") && m.Contains("false"));
    }

    [Fact]
    public void RepeatedLosses_OnlyTellThePageOnce()
    {
        Transition(ScreenClientState.Connected, ScreenClientState.Disconnected);
        Transition(ScreenClientState.Disconnected, ScreenClientState.Error);

        Assert.Single(_sentToPage.Where(m => m.Contains("hostLost")));
    }

    private void Transition(ScreenClientState from, ScreenClientState to)
        => _client.StateChanged += Raise.EventWith(
            _client, new ScreenClientStateChangedEventArgs { OldState = from, NewState = to });

    public async ValueTask DisposeAsync() => await _controller.DisposeAsync();
}
