using KHost.Domain.Services.VideoEncoding;

namespace KHost.UnitTests.Domain.Services.VideoEncoding;

/// <summary>The exact command line each encoder is driven with: rate control, profile, level, pixel
/// format, and keyframes forced on time.</summary>
public class VideoEncoderProfileTests
{
    private const string Keyframes = " -force_key_frames \"expr:gte(t,n_forced*2)\"";

    /// <summary>Byte for byte what the host encoded with before any hardware encoder existed.</summary>
    [Theory]
    [InlineData(0, "4.1")]
    [InlineData(1080, "4.1")]
    [InlineData(2160, "5.1")]
    public void Arguments_Software_IsTheLibx264LineUnchanged(int height, string level)
        => Assert.Equal(
            $" -c:v libx264 -preset veryfast -profile:v main -level {level} -pix_fmt yuv420p{Keyframes} -sc_threshold 0",
            VideoEncoderProfile.Software.Arguments(height, 2));

    [Theory]
    [InlineData(1080, " -level 4.1", "12M")]
    [InlineData(2160, " -level 5.1", "40M")]
    [InlineData(0, "", "12M")]
    public void Arguments_VideoToolbox(int height, string level, string ceiling)
        => Assert.Equal(
            $" -c:v h264_videotoolbox -profile:v main{level} -pix_fmt nv12 -q:v 75 -maxrate {ceiling} -g 240{Keyframes}",
            VideoEncoderProfile.VideoToolbox.Arguments(height, 2));

    [Theory]
    [InlineData(1080, " -level 41")]
    [InlineData(2160, " -level 51")]
    [InlineData(0, "")]
    public void Arguments_QuickSync(int height, string level)
        => Assert.Equal(
            $" -c:v h264_qsv -preset veryfast -profile:v main{level} -pix_fmt nv12 -global_quality 25 -g 240 -forced_idr 1{Keyframes}",
            VideoEncoderProfile.QuickSync.Arguments(height, 2));

    [Theory]
    [InlineData(1080, " -level 4.1", "12M", "24M")]
    [InlineData(2160, " -level 5.1", "40M", "80M")]
    [InlineData(0, "", "12M", "24M")]
    public void Arguments_Nvenc(int height, string level, string ceiling, string buffer)
        => Assert.Equal(
            $" -c:v h264_nvenc -preset p4 -rc vbr -cq 23 -b:v 0 -maxrate {ceiling} -bufsize {buffer} -profile:v main{level}"
            + $" -pix_fmt nv12 -g 240 -forced-idr 1 -no-scenecut 1{Keyframes}",
            VideoEncoderProfile.Nvenc.Arguments(height, 2));

    [Theory]
    [InlineData(1080, " -level 4.1")]
    [InlineData(2160, " -level 5.1")]
    [InlineData(0, "")]
    public void Arguments_Amf(int height, string level)
        => Assert.Equal(
            $" -c:v h264_amf -quality speed -rc cqp -qp_i 22 -qp_p 24 -profile:v main{level} -pix_fmt nv12 -g 240 -forced_idr 1{Keyframes}",
            VideoEncoderProfile.Amf.Arguments(height, 2));

    [Theory]
    [InlineData(1080, "8M")]
    [InlineData(2160, "20M")]
    public void Arguments_MediaFoundation(int height, string bitrate)
        => Assert.Equal(
            $" -c:v h264_mf -hw_encoding 1 -rate_control u_vbr -b:v {bitrate} -pix_fmt nv12 -g 240{Keyframes}",
            VideoEncoderProfile.MediaFoundation.Arguments(height, 2));

    /// <summary>The keyframe interval and the GOP ceiling both follow the segment length.</summary>
    [Fact]
    public void Arguments_Hardware_ScaleTheKeyframesAndCeilingWithTheSegment()
    {
        var arguments = VideoEncoderProfile.VideoToolbox.Arguments(1080, 4);

        Assert.Contains(" -g 480 ", arguments);
        Assert.EndsWith(" -force_key_frames \"expr:gte(t,n_forced*4)\"", arguments);
    }
}
