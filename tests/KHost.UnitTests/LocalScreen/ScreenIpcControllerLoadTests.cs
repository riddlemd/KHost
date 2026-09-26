using System.Text.Json;
using KHost.IPC.SignalR.Contracts;
using KHost.LocalScreen;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace KHost.UnitTests.LocalScreen;

/// <summary>A load from the host reaches the page with everything the page draws it by.</summary>
public class ScreenIpcControllerLoadTests : IAsyncDisposable
{
    private readonly IScreenClient _client = Substitute.For<IScreenClient>();
    private readonly StreamMediaPlayer _player = new(NullLogger<StreamMediaPlayer>.Instance);
    private readonly List<string> _sentToPage = [];
    private readonly ScreenIpcController _controller;

    public ScreenIpcControllerLoadTests()
    {
        _player.SendToBrowser = _sentToPage.Add;
        _controller = new ScreenIpcController(_client, _player, NullLogger<ScreenIpcController>.Instance);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ALoad_TellsThePageWhetherThePictureIsGraphicsOnly(bool graphicsOnly)
    {
        _client.CommandReceived += Raise.EventWith(_client, new ScreenCommandReceivedEventArgs
        {
            Command = new LoadMediaCommand { StreamUrl = "http://host/s.m3u8", IsGraphicsOnly = graphicsOnly },
        });

        // The handler is async void, so the page hears it on its own schedule.
        for (var i = 0; i < 100 && !_sentToPage.Any(m => m.Contains("\"load\"")); i++) await Task.Delay(10);

        var load = JsonDocument.Parse(Assert.Single(_sentToPage, m => m.Contains("\"load\""))).RootElement;
        Assert.Equal(graphicsOnly, load.GetProperty("pixelated").GetBoolean());
    }

    public async ValueTask DisposeAsync() => await _controller.DisposeAsync();
}
