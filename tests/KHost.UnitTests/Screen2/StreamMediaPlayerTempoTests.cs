using System.Text.Json;
using KHost.Screen2;
using Microsoft.Extensions.Logging.Abstractions;

namespace KHost.UnitTests.Screen2;

/// <summary>
/// The page counts in stream seconds and knows nothing of tempo, so this class is the only place a
/// retimed stream is turned back into song time. Getting it wrong is silent: the screen plays, and
/// only the playhead and the sync correction are quietly wrong.
/// </summary>
public class StreamMediaPlayerTempoTests
{
    private readonly StreamMediaPlayer _player = new(NullLogger<StreamMediaPlayer>.Instance);
    private readonly List<string> _sentToPage = [];

    public StreamMediaPlayerTempoTests() => _player.SendToBrowser = _sentToPage.Add;

    [Fact]
    public void SetTimeline_ConvertsTheSongPositionIntoStreamSeconds()
    {
        _player.LoadStream("http://host/s/stream.m3u8", TimeSpan.FromSeconds(30), tempo: 50);

        _player.SetTimeline(TimeSpan.FromSeconds(90), DateTime.UtcNow, isPlaying: true, isPrimary: false);

        // 60 song seconds past the stream's zero, which at 1.5x is 40 seconds of stream.
        Assert.Equal(40.0, LastValue("timeline", "position"), 3);
    }

    [Fact]
    public void Seek_ConvertsTheTargetIntoStreamSeconds()
    {
        _player.LoadStream("http://host/s/stream.m3u8", TimeSpan.FromSeconds(10), tempo: -50);

        _player.Seek(TimeSpan.FromSeconds(40));

        // 30 song seconds past zero at half speed is 60 seconds of stream.
        Assert.Equal(60.0, LastValue("seek", "position"), 3);
    }

    [Fact]
    public void ReportedPosition_ScalesStreamSecondsBackIntoSongTime()
    {
        _player.LoadStream("http://host/s/stream.m3u8", TimeSpan.FromSeconds(30), tempo: 50);

        _player.HandleBrowserMessage(
            """{"type":"state","position":40,"playing":true,"duration":80}""");

        Assert.Equal(TimeSpan.FromSeconds(90), _player.Position);
        Assert.Equal(TimeSpan.FromSeconds(150), _player.Duration);
    }

    [Fact]
    public void LoadStream_AtTheRecordedTempo_LeavesPositionsAlone()
    {
        _player.LoadStream("http://host/s/stream.m3u8", TimeSpan.FromSeconds(30));

        _player.SetTimeline(TimeSpan.FromSeconds(90), DateTime.UtcNow, isPlaying: true, isPrimary: false);
        _player.HandleBrowserMessage("""{"type":"state","position":60,"playing":true}""");

        Assert.Equal(60.0, LastValue("timeline", "position"), 3);
        Assert.Equal(TimeSpan.FromSeconds(90), _player.Position);
    }

    [Fact]
    public void LoadStream_ForgetsThePreviousStreamsTempo()
    {
        _player.LoadStream("http://host/s/first.m3u8", TimeSpan.Zero, tempo: 50);
        _player.LoadStream("http://host/s/second.m3u8", TimeSpan.Zero);

        _player.HandleBrowserMessage("""{"type":"state","position":60,"playing":true}""");

        // A reopen at the recorded tempo has to clear the old rate, or the playhead runs away.
        Assert.Equal(TimeSpan.FromSeconds(60), _player.Position);
    }

    /// <summary>Reads one number out of the last command of a type the page was sent.</summary>
    private double LastValue(string type, string property)
    {
        var message = _sentToPage.Last(m =>
            JsonDocument.Parse(m).RootElement.GetProperty("type").GetString() == type);

        return JsonDocument.Parse(message).RootElement.GetProperty(property).GetDouble();
    }
}
