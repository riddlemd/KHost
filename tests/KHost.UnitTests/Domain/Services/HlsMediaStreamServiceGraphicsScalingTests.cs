using KHost.Domain.Services;
using KHost.Domain.Services.BurnIn;

namespace KHost.UnitTests.Domain.Services;

/// <summary>A .cdg scaled up to the height the host chose at its own shape, with no bands, laid on black so a disc
/// that never draws still has a picture, and held until its audio ends.</summary>
public class HlsMediaStreamServiceGraphicsScalingTests
{
    /// <summary>The whole chain, in order: hold, fps, canvas, then the scale. fps ahead of the scale
    /// halves the CPU, since a .cdg decodes up to 300 frames a second while it draws.</summary>
    [Theory]
    [InlineData(1080, "scale=iw*5:ih*5:flags=neighbor,")]
    [InlineData(2160, "scale=iw*10:ih*10:flags=neighbor,")]
    [InlineData(720, "scale=iw*3:ih*3:flags=neighbor,scale=1000:720:flags=bicubic,")]
    public void BuildArguments_ScalesGraphicsToTheChosenHeightWithNoBands(int height, string scale)
    {
        var arguments = HlsMediaStreamService.BuildArguments(
            "/songs/a.cdg", TimeSpan.Zero, 0, 0, 2, "/songs/a.mp3", graphicsHeight: height);

        Assert.Contains(
            "-filter_complex \"color=c=black:s=300x216:r=30[canvas];"
            + "[0:v:0]tpad=stop=-1:stop_mode=clone,fps=30[graphics];"
            + $"[canvas][graphics]overlay=format=rgb,{scale}setsar=1[v]\" -map \"[v]\" -map 1:a:0",
            arguments);
        Assert.Contains(" -shortest ", arguments);
        Assert.DoesNotContain(",pad=", arguments);
    }

    /// <summary>The retime comes first, so the frame rate and everything after it run in output time.</summary>
    [Fact]
    public void BuildArguments_RetimesGraphicsBeforeBringingThemToTheFrameRate()
    {
        var arguments = HlsMediaStreamService.BuildArguments(
            "/songs/a.cdg", TimeSpan.Zero, 0, 20, 2, "/songs/a.mp3", graphicsHeight: 720);

        Assert.Contains(
            "[0:v:0]tpad=stop=-1:stop_mode=clone,setpts=PTS/1.200000,fps=30[graphics];"
            + "[canvas][graphics]overlay=format=rgb,scale=iw*3:ih*3:flags=neighbor,",
            arguments);
        Assert.Contains(" -af \"", arguments);
    }

    [Fact]
    public void BuildArguments_Off_KeepsTheNativePicture()
    {
        var arguments = HlsMediaStreamService.BuildArguments(
            "/songs/a.cdg", TimeSpan.Zero, 0, 0, 2, "/songs/a.mp3", graphicsHeight: GraphicsScaling.Off);

        Assert.Contains("[canvas][graphics]overlay=format=rgb,setsar=1[v]", arguments);
        Assert.DoesNotContain("scale=", arguments);
    }

    /// <summary>Real video was measured not to gain from an upscale, so it passes through as it is.</summary>
    [Fact]
    public void BuildArguments_NeverScalesAnOrdinaryVideo()
    {
        var arguments = HlsMediaStreamService.BuildArguments(
            "/songs/a.mp4", TimeSpan.Zero, 0, 0, 2, graphicsHeight: 1080);

        Assert.DoesNotContain("scale=", arguments);
        Assert.DoesNotContain("color=", arguments);
    }

    /// <summary>With no audio there is nothing to end a held picture, so it is scaled without one.</summary>
    [Fact]
    public void BuildArguments_ScalesGraphicsWithNoAudioWithoutHoldingThem()
    {
        var arguments = HlsMediaStreamService.BuildArguments(
            "/songs/a.cdg", TimeSpan.Zero, 0, 0, 2, graphicsHeight: 720);

        Assert.Contains("-vf \"fps=30,scale=iw*3:ih*3:flags=neighbor,scale=1000:720:flags=bicubic,setsar=1\"", arguments);
        Assert.DoesNotContain("tpad", arguments);
        Assert.DoesNotContain("-shortest", arguments);
    }

    /// <summary>Level 4.1 cannot describe a frame taller than 1080 lines.</summary>
    [Theory]
    [InlineData(1080, "-level 4.1 ")]
    [InlineData(2160, "-level 5.1 ")]
    public void BuildArguments_RaisesTheLevel_OnlyAbove1080Lines(int height, string level)
    {
        var arguments = HlsMediaStreamService.BuildArguments(
            "/songs/a.cdg", TimeSpan.Zero, 0, 0, 2, "/songs/a.mp3", graphicsHeight: height);

        Assert.Contains(level, arguments);
    }

    /// <summary>The words go over a .cdg scaled by the same whole number as without them.</summary>
    [Fact]
    public void BuildArguments_BurningInGraphics_ScalesThemIntoTheBurnInFrame()
    {
        var overlay = new BurnInOverlay(1920, 1080, 30, BurnInBase.SourceVideo);

        var arguments = HlsMediaStreamService.BuildArguments(
            "/songs/a.cdg", TimeSpan.Zero, 0, 0, 2, "/songs/a.mp3", burnIn: overlay, graphicsHeight: 720);

        Assert.Contains(" -s 1920x1080 ", arguments);
        Assert.Contains(
            "[canvas][graphics]overlay=format=rgb,scale=iw*5:ih*5:flags=neighbor,"
            + "pad=1920:1080:(ow-iw)/2:(oh-ih)/2,setsar=1[base];[base][2:v]overlay=0:0:eof_action=pass[v]",
            arguments);
        Assert.Contains(" -shortest ", arguments);
    }

    [Fact]
    public void BuildArguments_BurningInGraphicsWithNoAudio_StillScalesThemOnWholePixels()
    {
        var overlay = new BurnInOverlay(1280, 720, 30, BurnInBase.SourceVideo);

        var arguments = HlsMediaStreamService.BuildArguments(
            "/songs/a.cdg", TimeSpan.Zero, 0, 0, 2, burnIn: overlay);

        Assert.Contains("[0:v:0]fps=30,scale=iw*3:ih*3:flags=neighbor,pad=1280:720:(ow-iw)/2:(oh-ih)/2,setsar=1[base]", arguments);
    }

    [Fact]
    public void ServiceOptions_GraphicsScaleHeight_DefaultsToOff()
        => Assert.Equal(GraphicsScaling.Off, new HlsMediaStreamService.ServiceOptions().GraphicsScaleHeight);

    /// <summary>Burned-in words always need a frame, so scaling off still paints at 720p.</summary>
    [Fact]
    public void BurnInFrame_WithScalingOff_Is720p()
        => Assert.Equal((1280, 720), BurnInOverlay.FrameFor(GraphicsScaling.Off));

    [Fact]
    public void BurnInFrame_FollowsTheChosenFrame_UpToTheCap()
        => Assert.Equal(GraphicsScaling.FrameOfHeight(BurnInOverlay.MaxHeight), BurnInOverlay.FrameFor(BurnInOverlay.MaxHeight));

    /// <summary>Measured: a 1080p burn-in reopened two minutes in outran the playlist timeout.</summary>
    [Theory]
    [InlineData(1080)]
    [InlineData(2160)]
    public void BurnInFrame_AboveTheCap_PaintsAt720p(int height)
        => Assert.Equal((1280, 720), BurnInOverlay.FrameFor(height));
}
