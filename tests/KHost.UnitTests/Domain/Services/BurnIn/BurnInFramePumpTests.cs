using KHost.Abstractions.Models;
using KHost.Domain.Services.BurnIn;

namespace KHost.UnitTests.Domain.Services.BurnIn;

public class BurnInFramePumpTests
{
    private static readonly TimedLyrics Nothing = new() { DurationSeconds = 1, Bounds = new LyricBox(0, 0, 64, 36) };

    /// <summary>Output time runs at the tempo's rate against the song, so the words keep pace with
    /// audio sped up or slowed down by the same factor.</summary>
    [Theory]
    [InlineData(0, 12.0, 1.0, 12.0)]
    [InlineData(30, 12.0, 1.0, 13.0)]
    [InlineData(30, 12.0, 1.1, 13.1)]
    [InlineData(60, 0.0, 0.9, 1.8)]
    public void SongTimeOf_StartsAtThePlayheadAndRunsAtTheRate(int frame, double start, double rate, double expected)
        => Assert.Equal(expected, BurnInFramePump.SongTimeOf(frame, start, rate, 30), 6);

    [Theory]
    [InlineData(10.0, 0.0, 1.0, 300)]
    [InlineData(10.0, 4.0, 1.0, 180)]
    [InlineData(10.0, 4.0, 2.0, 90)]
    [InlineData(10.0, 12.0, 1.0, 0)]
    public void FramesFor_CoversTheSongFromThePlayheadAtTheRate(double end, double start, double rate, int expected)
        => Assert.Equal(expected, BurnInFramePump.FramesFor(end, start, rate, 30));

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void Run_WritesEveryFrameWholeAndThenClosesThePipe(int workers)
    {
        var painter = new TimedLyricsPainter(Nothing, 64, 36);
        var pipe = new ClosingStream();

        BurnInFramePump.Run(painter, pipe, 0, 1, 30, 7, workers, CancellationToken.None);

        Assert.Equal(7 * 64 * 36 * 4, pipe.Written);
        Assert.True(pipe.Closed);
    }

    /// <summary>The encode ending, or the session going, closes the pipe under the painters; that is
    /// the end of the song, not a fault.</summary>
    [Fact]
    public void Run_StopsQuietly_WhenTheReaderGoesAway()
    {
        var painter = new TimedLyricsPainter(Nothing, 64, 36);
        var pipe = new ClosingStream { BreakAfter = 3 * 64 * 36 * 4 };

        BurnInFramePump.Run(painter, pipe, 0, 1, 30, 1000, 2, CancellationToken.None);

        Assert.Equal(3 * 64 * 36 * 4, pipe.Written);
    }

    private sealed class ClosingStream : Stream
    {
        public long Written { get; private set; }
        public long BreakAfter { get; init; } = long.MaxValue;
        public bool Closed { get; private set; }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => Written;
        public override long Position { get => Written; set => throw new NotSupportedException(); }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (Written + count > BreakAfter) throw new IOException("Broken pipe");
            Written += count;
        }

        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            Closed = true;
            base.Dispose(disposing);
        }
    }
}
