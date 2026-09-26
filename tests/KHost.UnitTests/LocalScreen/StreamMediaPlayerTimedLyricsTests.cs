using System.Text.Json;
using KHost.Abstractions.Models;
using KHost.IPC.SignalR.Contracts;
using KHost.LocalScreen;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.LocalScreen;

/// <summary>The page reads these by name, so a field dropped here is a card that silently never draws.</summary>
public class StreamMediaPlayerTimedLyricsTests
{
    private readonly StreamMediaPlayer _player = new(NullLogger<StreamMediaPlayer>.Instance);
    private readonly List<string> _sentToPage = [];

    public StreamMediaPlayerTimedLyricsTests() => _player.SendToBrowser = _sentToPage.Add;

    [Fact]
    public void SetTimedLyrics_WithAnIntroCard_CarriesItToThePage()
    {
        _player.SetTimedLyrics(new SetTimedLyricsCommand
        {
            Lyrics = new TimedLyrics { DurationSeconds = 90, Bounds = new LyricBox(0, 0, 640, 360) },
            Intro = new ScreenIntroCard { Title = "Africa", Artist = "Toto", Singer = "DJ P" },
        });

        var message = JsonDocument.Parse(_sentToPage[^1]).RootElement;
        var intro = message.GetProperty("intro");

        Assert.Equal("timed-lyrics", message.GetProperty("type").GetString());
        Assert.Equal("Africa", intro.GetProperty("title").GetString());
        Assert.Equal("Toto", intro.GetProperty("artist").GetString());
        Assert.Equal("DJ P", intro.GetProperty("singer").GetString());
    }

    [Fact]
    public void SetTimedLyrics_WithALeadIn_CarriesItToThePage()
    {
        _player.SetTimedLyrics(new SetTimedLyricsCommand
        {
            Lyrics = new TimedLyrics { DurationSeconds = 90, Bounds = new LyricBox(0, 0, 640, 360) },
            LeadInSeconds = 3.8,
        });

        var message = JsonDocument.Parse(_sentToPage[^1]).RootElement;

        Assert.Equal(3.8, message.GetProperty("leadInSeconds").GetDouble());
    }

    [Fact]
    public void SetTimedLyrics_WithNoIntroCard_SendsNone()
    {
        _player.SetTimedLyrics(new SetTimedLyricsCommand { Lyrics = null });

        var message = JsonDocument.Parse(_sentToPage[^1]).RootElement;

        Assert.False(message.TryGetProperty("intro", out var intro) && intro.ValueKind != JsonValueKind.Null);
    }
}
