using KHost.Abstractions.Exceptions;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services;
using NSubstitute;

namespace KHost.UnitTests.Domain.Services;

public class CompactDiscPlusGraphicsRendererTests
{
    private readonly IMediaStreamService _streams = Substitute.For<IMediaStreamService>();

    [Theory]
    [InlineData("/songs/a.cdg", true)]
    [InlineData("/songs/A.CDG", true)]
    [InlineData("/songs/a.zip", true)]
    [InlineData("/songs/A.ZIP", true)]
    [InlineData("/songs/a.mp3", false)]
    [InlineData("/songs/a.mp4", false)]
    public void CanRender_ClaimsCompactDiscGraphicsLooseOrZippedAndNothingElse(string path, bool expected)
        => Assert.Equal(expected, new CompactDiscPlusGraphicsRenderer(_streams).CanRender(path));

    [Fact]
    public async Task RenderAsync_RefusesACdgWithNoAudioBesideIt()
    {
        var directory = Directory.CreateTempSubdirectory("khost-cdg").FullName;

        try
        {
            var cdg = Path.Combine(directory, "a.cdg");
            await File.WriteAllBytesAsync(cdg, [1]);

            // Half a song. It used to encode and reach the room as silence, which is the one
            // symptom that never points at its own cause.
            var thrown = await Assert.ThrowsAsync<KHostException>(
                () => new CompactDiscPlusGraphicsRenderer(_streams).RenderAsync(Request(cdg)));

            Assert.Equal("KH-CDG-NO-AUDIO", thrown.ReferenceCode);

            // And nothing was started for it: refusing must not leave an ffmpeg behind.
            await _streams.DidNotReceiveWithAnyArgs().OpenAsync(default!);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task RenderAsync_RendersThroughTheStream_WhenTheAudioIsBesideIt()
    {
        var directory = Directory.CreateTempSubdirectory("khost-cdg").FullName;

        try
        {
            var cdg = Path.Combine(directory, "a.cdg");
            await File.WriteAllBytesAsync(cdg, [1]);
            await File.WriteAllBytesAsync(Path.Combine(directory, "a.mp3"), [1]);

            _streams.OpenAsync(cdg, Arg.Any<TimeSpan>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<AudioMix?>(), Arg.Any<CancellationToken>())
                .Returns(new MediaStreamSession
                {
                    Id = "session-1",
                    SourcePath = cdg,
                    PlaylistUrl = "http://host/media/session-1/stream.m3u8",
                    StartOffset = TimeSpan.Zero,
                    Pitch = 0,
                    Tempo = 0,
                });

            var rendition = await new CompactDiscPlusGraphicsRenderer(_streams).RenderAsync(Request(cdg));

            // Subcode graphics still have to be decoded into a picture, so the encode is inherited
            // rather than replaced — until the day a screen draws them itself.
            Assert.NotNull(rendition);
            Assert.Equal("http://host/media/session-1/stream.m3u8", rendition.Url);
            Assert.False(rendition.SeekableInPlace);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    /// <summary>A zip that is not one song fails with its own reason before any encode opens.</summary>
    [Fact]
    public async Task RenderAsync_RefusesAZipThatIsNotOneSong()
    {
        var directory = Directory.CreateTempSubdirectory("khost-cdg").FullName;

        try
        {
            var zip = KaraokeZipFixture.Write(directory, "a.zip", ("a.cdg", KaraokeZipFixture.Graphics));

            var thrown = await Assert.ThrowsAsync<KHostException>(
                () => new CompactDiscPlusGraphicsRenderer(_streams).RenderAsync(Request(zip)));

            Assert.Equal(KaraokeZip.ShapeCode, thrown.ReferenceCode);
            await _streams.DidNotReceiveWithAnyArgs().OpenAsync(default!);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    /// <summary>The zip itself goes to the encode; the playable source writes the pair out there.</summary>
    [Fact]
    public async Task RenderAsync_RendersAZippedPairThroughTheStream()
    {
        var directory = Directory.CreateTempSubdirectory("khost-cdg").FullName;

        try
        {
            var zip = KaraokeZipFixture.Write(directory, "a.zip",
                ("a.cdg", KaraokeZipFixture.Graphics), ("a.mp3", KaraokeZipFixture.Audio));

            _streams.OpenAsync(zip, Arg.Any<TimeSpan>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<AudioMix?>(), Arg.Any<CancellationToken>())
                .Returns(new MediaStreamSession
                {
                    Id = "session-1",
                    SourcePath = zip,
                    PlaylistUrl = "http://host/media/session-1/stream.m3u8",
                    StartOffset = TimeSpan.Zero,
                    Pitch = 0,
                    Tempo = 0,
                });

            var rendition = await new CompactDiscPlusGraphicsRenderer(_streams).RenderAsync(Request(zip));

            Assert.Equal("http://host/media/session-1/stream.m3u8", rendition?.Url);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static MediaRenderRequest Request(string filePath) => new()
    {
        FilePath = filePath,
        Target = RenderTarget.None,
    };
}
