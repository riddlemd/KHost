using KHost.Abstractions.Exceptions;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services;
using NSubstitute;

namespace KHost.UnitTests.Domain.Services;

public class GraphicsKaraokeRendererTests
{
    private readonly IMediaStreamService _streams = Substitute.For<IMediaStreamService>();

    [Theory]
    [InlineData("/songs/a.cdg", true)]
    [InlineData("/songs/A.CDG", true)]
    [InlineData("/songs/a.mp3", false)]
    [InlineData("/songs/a.mp4", false)]
    public void CanRender_ClaimsTheGraphicsHalfAndNothingElse(string path, bool expected)
        => Assert.Equal(expected, new GraphicsKaraokeRenderer(_streams).CanRender(path));

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
                () => new GraphicsKaraokeRenderer(_streams).RenderAsync(Request(cdg)));

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

            var rendition = await new GraphicsKaraokeRenderer(_streams).RenderAsync(Request(cdg));

            // Subcode graphics still have to be decoded into a picture, so the encode is inherited
            // rather than replaced — until the day a screen draws them itself.
            Assert.NotNull(rendition);
            Assert.Equal("http://host/media/session-1/stream.m3u8", rendition.Url);
            Assert.False(rendition.SeekableInPlace);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static MediaRenderRequest Request(string filePath) => new()
    {
        FilePath = filePath,
        Target = RenderTarget.None,
    };
}
