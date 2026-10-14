using KHost.Abstractions.Models;
using KHost.IPC.SignalR.Contracts;
using KHost.LocalScreen;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace KHost.UnitTests.LocalScreen;

/// <summary>Only a transport command answers with playback state; an overlay command changes nothing
/// the host's clock tracks, so a report would be noise.</summary>
public class ScreenIpcControllerStateReportTests : IAsyncDisposable
{
    private readonly IScreenClient _client = Substitute.For<IScreenClient>();
    private readonly StreamMediaPlayer _player = new(NullLogger<StreamMediaPlayer>.Instance);
    private readonly List<IScreenState> _sentToHost = [];
    private readonly ScreenIpcController _controller;

    public ScreenIpcControllerStateReportTests()
    {
        _client.SendStateAsync(Arg.Do<IScreenState>(_sentToHost.Add)).Returns(Task.CompletedTask);
        _controller = new ScreenIpcController(_client, _player, NullLogger<ScreenIpcController>.Instance);
    }

    public async ValueTask DisposeAsync()
    {
        await _controller.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    private void Receive(IScreenCommand command)
        => _client.CommandReceived += Raise.EventWith(new ScreenCommandReceivedEventArgs { Command = command });

    public static TheoryData<IScreenCommand> TransportCommands => new()
    {
        new LoadMediaCommand { StreamUrl = "http://host/media/a/stream.m3u8" },
        new PlayCommand(),
        new PauseCommand(),
        new StopCommand { FadeDuration = TimeSpan.Zero },
        new SeekCommand { Position = TimeSpan.FromSeconds(5) },
        new SetVolumeCommand { Volume = 0.5f },
        new SetStemVolumeCommand { Role = AudioTrackRole.Lead, Volume = 40 },
    };

    public static TheoryData<IScreenCommand> OverlayCommands => new()
    {
        new SetMarqueeCommand { Enabled = false },
        new HideImageCommand(),
        new ShowImageCommand { Url = "http://host/media/image/abc", Scaling = ImageScaling.Fill },
    };

    [Theory]
    [MemberData(nameof(TransportCommands))]
    public void ATransportCommand_AnswersWithExactlyOnePlaybackState(IScreenCommand command)
    {
        Receive(command);

        Assert.IsType<ScreenPlaybackState>(Assert.Single(_sentToHost));
    }

    [Theory]
    [MemberData(nameof(OverlayCommands))]
    public void AnOverlayCommand_SendsNothingBack(IScreenCommand command)
    {
        Receive(command);

        Assert.Empty(_sentToHost);
    }
}
