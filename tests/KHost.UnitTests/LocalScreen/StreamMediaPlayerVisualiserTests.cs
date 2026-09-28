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
    [InlineData(true, "Rovastar - Oozing Resistance", null)]
    [InlineData(true, null, "http://host/media/visualiser-presets/Mine?v=1")]
    [InlineData(false, null, null)]
    public void SetVisualiser_TellsThePageWhetherAndWhichPreset(bool enabled, string? name, string? url)
    {
        _player.SetVisualiser(new SetVisualiserCommand { Enabled = enabled, PresetName = name, PresetUrl = url });

        var message = JsonDocument.Parse(Assert.Single(_sentToPage)).RootElement;

        Assert.Equal("visualiser", message.GetProperty("type").GetString());
        Assert.Equal(enabled, message.GetProperty("enabled").GetBoolean());
        Assert.Equal(name, message.GetProperty("presetName").GetString());
        Assert.Equal(url, message.GetProperty("presetUrl").GetString());
    }

    [Fact]
    public void SetVisualiser_PassesOnHowToDrawIt()
    {
        _player.SetVisualiser(new SetVisualiserCommand { Enabled = true, PresetName = "x", Brightness = 80, Saturation = 150, Sensitivity = 250 });

        var message = JsonDocument.Parse(Assert.Single(_sentToPage)).RootElement;

        Assert.Equal(80, message.GetProperty("brightness").GetInt32());
        Assert.Equal(150, message.GetProperty("saturation").GetInt32());
        Assert.Equal(250, message.GetProperty("sensitivity").GetInt32());
    }

    [Theory]
    [InlineData("http://host/media/levels/abc")]
    [InlineData(null)]
    public void SetVisualiser_PassesOnWhereTheHostsLevelsAre(string? url)
    {
        _player.SetVisualiser(new SetVisualiserCommand { Enabled = true, PresetName = "x", LevelsUrl = url });

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
