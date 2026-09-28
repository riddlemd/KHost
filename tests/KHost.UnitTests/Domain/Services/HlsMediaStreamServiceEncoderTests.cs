using KHost.Abstractions.Models;
using KHost.Domain.Services;
using KHost.Domain.Services.BurnIn;
using KHost.Domain.Services.VideoEncoding;
using StemInput = KHost.Domain.Services.HlsMediaStreamService.StemInput;

namespace KHost.UnitTests.Domain.Services;

/// <summary>Every encode path cuts its picture with the chosen encoder, and nothing else in the
/// command line moves: the filters run on the CPU ahead of it either way.</summary>
public class HlsMediaStreamServiceEncoderTests
{
    private static readonly VideoEncoderProfile Hardware = VideoEncoderProfile.VideoToolbox;

    [Fact]
    public void BuildArguments_NoEncoderGiven_IsLibx264()
        => Assert.Equal(
            HlsMediaStreamService.BuildArguments("/songs/a.mp4", TimeSpan.Zero, 0, 0, 2, encoder: VideoEncoderProfile.Software),
            HlsMediaStreamService.BuildArguments("/songs/a.mp4", TimeSpan.Zero, 0, 0, 2));

    /// <summary>A source video's size is unknown here, so the hardware line names no level.</summary>
    [Fact]
    public void BuildArguments_Video_SwapsOnlyTheEncoder()
        => AssertOnlyTheEncoderMoved(
            encoder => HlsMediaStreamService.BuildArguments("/songs/a.mp4", TimeSpan.FromSeconds(5), 2, 10, 2, encoder: encoder),
            frameHeight: 0);

    [Theory]
    [InlineData(0)]
    [InlineData(1080)]
    [InlineData(2160)]
    public void BuildArguments_Graphics_SwapsOnlyTheEncoder_AtTheScaledLevel(int height)
        => AssertOnlyTheEncoderMoved(
            encoder => HlsMediaStreamService.BuildArguments(
                "/songs/a.cdg", TimeSpan.Zero, 0, 0, 2, "/songs/a.mp3", graphicsHeight: height, encoder: encoder),
            frameHeight: height);

    [Fact]
    public void BuildArguments_BurningIn_SwapsOnlyTheEncoder()
        => AssertOnlyTheEncoderMoved(
            encoder => HlsMediaStreamService.BuildArguments(
                "/songs/a.mp4", TimeSpan.Zero, 0, 0, 2, burnIn: new BurnInOverlay(1920, 1080, 30, BurnInBase.SourceVideo),
                encoder: encoder),
            frameHeight: 1080);

    [Fact]
    public void BuildArguments_UnderDrawnWords_Video_SwapsOnlyTheEncoder()
        => AssertOnlyTheEncoderMoved(
            encoder => HlsMediaStreamService.BuildArguments(
                "/songs/a.mp4", TimeSpan.Zero, 0, 0, 2, hasTimedLyrics: true, encoder: encoder),
            frameHeight: 0);

    /// <summary>An audio file under drawn words carries no picture, so there is nothing to encode
    /// and nothing for a failed hardware start to retry.</summary>
    [Fact]
    public void BuildArguments_UnderDrawnWords_AudioSource_NamesNoEncoder()
        => Assert.DoesNotContain(
            "-c:v",
            HlsMediaStreamService.BuildArguments(
                "/songs/a.mp3", TimeSpan.Zero, 0, 0, 2, hasTimedLyrics: true, encoder: Hardware));

    [Fact]
    public void BuildStemArguments_WithAPicture_SwapsOnlyTheEncoder()
        => AssertOnlyTheEncoderMoved(
            encoder => HlsMediaStreamService.BuildStemArguments(
                [new StemInput("/s/music.ogg", AudioTrackRole.Music, 100)], TimeSpan.Zero, 0, 0, 2,
                new BurnInOverlay(3840, 2160, 30, BurnInBase.Fill), encoder),
            frameHeight: 2160);

    [Fact]
    public void BuildStemArguments_AudioAlone_NamesNoEncoder()
        => Assert.DoesNotContain(
            "-c:v",
            HlsMediaStreamService.BuildStemArguments(
                [new StemInput("/s/music.ogg", AudioTrackRole.Music, 100)], TimeSpan.Zero, 0, 0, 2, encoder: Hardware));

    private static void AssertOnlyTheEncoderMoved(Func<VideoEncoderProfile, string> build, int frameHeight)
    {
        var software = build(VideoEncoderProfile.Software);
        var hardware = build(Hardware);

        var softwareEncode = VideoEncoderProfile.Software.Arguments(frameHeight, 2);
        var hardwareEncode = Hardware.Arguments(frameHeight, 2);

        Assert.Contains(softwareEncode, software);
        Assert.Equal(software.Replace(softwareEncode, hardwareEncode), hardware);
    }
}
