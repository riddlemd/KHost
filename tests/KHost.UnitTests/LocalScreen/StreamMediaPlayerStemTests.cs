using System.Text.Json;
using KHost.Abstractions.Models;
using KHost.LocalScreen;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.LocalScreen;

/// <summary>The page matches a level to a stem by role and singer, so both have to reach it.</summary>
public class StreamMediaPlayerStemTests
{
    private readonly StreamMediaPlayer _player = new(NullLogger<StreamMediaPlayer>.Instance);
    private readonly List<string> _sentToPage = [];

    public StreamMediaPlayerStemTests() => _player.SendToBrowser = _sentToPage.Add;

    [Fact]
    public void LoadStream_TellsThePageWhichSingerEachStemBelongsTo()
    {
        _player.LoadStream(null, TimeSpan.Zero, stems:
        [
            new StemSource(0, AudioTrackRole.Music, "http://host/m.ogg", 100),
            new StemSource(1, AudioTrackRole.Lead, "http://host/l1.ogg", 0) { Voice = "♂" },
        ]);

        var stems = Last("load").GetProperty("stems");

        Assert.Equal(JsonValueKind.Null, stems[0].GetProperty("voice").ValueKind);
        Assert.Equal("♂", stems[1].GetProperty("voice").GetString());
    }

    [Theory]
    [InlineData("♀")]
    [InlineData(null)]
    public void SetStemVolume_TellsThePageWhichSingerTheLevelIsFor(string? voice)
    {
        _player.SetStemVolume(AudioTrackRole.Lead, voice, 45);

        var message = Last("stem-volume");

        Assert.Equal("Lead", message.GetProperty("role").GetString());
        Assert.Equal(voice, message.GetProperty("voice").GetString());
        Assert.Equal(45, message.GetProperty("volume").GetInt32());
    }

    private JsonElement Last(string type) => _sentToPage
        .Select(json => JsonDocument.Parse(json).RootElement)
        .Last(message => message.GetProperty("type").GetString() == type);
}
