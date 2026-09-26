using KHost.LocalScreen;
using Microsoft.Extensions.Logging;

namespace KHost.UnitTests.LocalScreen;

/// <summary>A screen gone silent after a sleep is explained only by these lines, so they must log.</summary>
public class StreamMediaPlayerWakeTests
{
    private readonly RecordingLogger _logger = new();
    private readonly StreamMediaPlayer _player;

    public StreamMediaPlayerWakeTests() => _player = new StreamMediaPlayer(_logger);

    [Fact]
    public void HandleBrowserMessage_Wake_LogsHowLongAndWhatWasHeldAtInformation()
    {
        var handled = _player.HandleBrowserMessage("""{"type":"wake","asleepSeconds":612,"holding":"stems"}""");

        Assert.True(handled);
        var (level, text) = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Information, level);
        Assert.Contains("612", text);
        Assert.Contains("stems", text);
    }

    [Theory]
    [InlineData(true, "playing")]
    [InlineData(false, "paused")]
    public void HandleBrowserMessage_AudioRebuilt_LogsWhereAndHowItCameBackAtInformation(bool playing, string expected)
    {
        var handled = _player.HandleBrowserMessage(
            $$"""{"type":"audio-rebuilt","position":42.5,"playing":{{(playing ? "true" : "false")}},"audioState":"running"}""");

        Assert.True(handled);
        var (level, text) = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Information, level);
        Assert.Contains("42.5", text);
        Assert.Contains(expected, text);
        Assert.Contains("running", text);
    }

    private sealed class RecordingLogger : ILogger<StreamMediaPlayer>
    {
        public List<(LogLevel Level, string Text)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, formatter(state, exception)));
    }
}
