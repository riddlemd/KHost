using System.Text.Json;
using KHost.IPC.SignalR.Contracts;
using KHost.LocalScreen;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.LocalScreen;

public class StreamMediaPlayerVisualiserTests
{
    private readonly StreamMediaPlayer _player = new(NullLogger<StreamMediaPlayer>.Instance);
    private readonly List<string> _sentToPage = [];

    public StreamMediaPlayerVisualiserTests() => _player.SendToBrowser = _sentToPage.Add;

    [Theory]
    [InlineData(true, 12)]
    [InlineData(false, 0)]
    public void SetVisualiser_TellsThePageWhetherAndWhichPreset(bool enabled, int preset)
    {
        _player.SetVisualiser(new SetVisualiserCommand { Enabled = enabled, Preset = preset });

        var message = JsonDocument.Parse(Assert.Single(_sentToPage)).RootElement;

        Assert.Equal("visualiser", message.GetProperty("type").GetString());
        Assert.Equal(enabled, message.GetProperty("enabled").GetBoolean());
        Assert.Equal(preset, message.GetProperty("preset").GetInt32());
    }

    [Theory]
    [InlineData("http://host/media/levels/abc")]
    [InlineData(null)]
    public void SetVisualiser_PassesOnWhereTheHostsLevelsAre(string? url)
    {
        _player.SetVisualiser(new SetVisualiserCommand { Enabled = true, Preset = 3, LevelsUrl = url });

        var message = JsonDocument.Parse(Assert.Single(_sentToPage)).RootElement;

        Assert.Equal(url, message.GetProperty("levels").GetString());
    }

    /// <summary>Passed on whatever the visualiser is doing: the band is the words', not the preset's.</summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void SetVisualiser_PassesOnWhetherToDarkenTheWordsBand(bool enabled, bool darken)
    {
        _player.SetVisualiser(new SetVisualiserCommand { Enabled = enabled, DarkenLyricBands = darken });

        var message = JsonDocument.Parse(Assert.Single(_sentToPage)).RootElement;

        Assert.Equal(darken, message.GetProperty("darken").GetBoolean());
    }
}
