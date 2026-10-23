using KHost.Abstractions.Models;
using KHost.Domain.Services;
using KHost.Domain.Services.BurnIn;
using KHost.Domain.Services.VideoEncoding;

namespace KHost.UnitTests.Domain.Services;

/// <summary>A display with no H.264 (Oriel on stock Chromium) gets VP9 with Opus in fMP4; every other
/// display keeps H.264 with AAC in MPEG-TS.</summary>
public class HlsOpenCodecArgumentsTests
{
    private static readonly EncodeInputs Video = new("/songs/a.mp4", TimeSpan.Zero, 0, 0, 2);

    [Fact]
    public void BuildArguments_Vp9_CutsVp9WithOpusInFmp4()
    {
        var line = HlsArguments.BuildArguments(Video, VideoEncoderProfile.Vp9);

        Assert.Contains("-c:v libvpx-vp9", line);
        Assert.Contains("-c:a libopus -ar 48000", line);
        Assert.Contains("-hls_segment_type fmp4 -hls_fmp4_init_filename init.mp4 -hls_segment_filename seg_%05d.m4s", line);
        Assert.DoesNotContain("libx264", line);
        Assert.DoesNotContain("-c:a aac", line);
        Assert.DoesNotContain(".ts", line);
    }

    /// <summary>The segments still cut on time, as the playlist timing depends on it.</summary>
    [Fact]
    public void BuildArguments_Vp9_ForcesKeyframesOnTheSegment()
        => Assert.Contains(
            "-force_key_frames \"expr:gte(t,n_forced*2)\"",
            HlsArguments.BuildArguments(Video, VideoEncoderProfile.Vp9));

    [Fact]
    public void BuildArguments_H264_KeepsAacInMpegTs()
    {
        var line = HlsArguments.BuildArguments(Video, VideoEncoderProfile.Software);

        Assert.Contains("-c:a aac -ar 44100", line);
        Assert.Contains("-hls_segment_filename seg_%05d.ts", line);
        Assert.DoesNotContain("fmp4", line);
        Assert.DoesNotContain("libopus", line);
    }

    /// <summary>No picture names no video encoder, but the audio and container still follow the display.</summary>
    [Fact]
    public void BuildArguments_Vp9_AudioUnderDrawnWords_IsOpusInFmp4WithNoVideo()
    {
        var line = HlsArguments.BuildArguments(
            new EncodeInputs("/songs/a.mp3", TimeSpan.Zero, 0, 0, 2) { DisplayDrawsWords = true }, VideoEncoderProfile.Vp9);

        Assert.DoesNotContain("-c:v", line);
        Assert.Contains("-c:a libopus", line);
        Assert.Contains("seg_%05d.m4s", line);
    }

    [Fact]
    public void BuildStemArguments_Vp9_AudioAlone_IsOpusInFmp4()
    {
        var line = HlsArguments.BuildStemArguments(
            [new StemInput("/s/music.ogg", AudioTrackRole.Music, 100)], TimeSpan.Zero, 0, 0, 2, encoder: VideoEncoderProfile.Vp9);

        Assert.DoesNotContain("-c:v", line);
        Assert.Contains("-c:a libopus", line);
        Assert.Contains("seg_%05d.m4s", line);
    }

    [Fact]
    public void BuildStemArguments_Vp9_BurningIn_CutsVp9()
        => Assert.Contains(
            "-c:v libvpx-vp9",
            HlsArguments.BuildStemArguments(
                [new StemInput("/s/music.ogg", AudioTrackRole.Music, 100)], TimeSpan.Zero, 0, 0, 2,
                new BurnInOverlay(1280, 720, 30, BurnInBase.Fill), VideoEncoderProfile.Vp9));
}
