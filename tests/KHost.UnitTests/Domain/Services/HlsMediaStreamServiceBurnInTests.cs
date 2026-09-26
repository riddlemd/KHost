using KHost.Abstractions.Models;
using KHost.Domain.Services;
using KHost.Domain.Services.BurnIn;

namespace KHost.UnitTests.Domain.Services;

/// <summary>The command line a burned-in encode runs: the painted frames read off a pipe and laid
/// over the picture inside the song's own encode, so key, tempo and the mix all still apply.</summary>
public class HlsMediaStreamServiceBurnInTests
{
    private static readonly BurnInOverlay OverSource = new(1280, 720, 30, BurnInBase.SourceVideo);
    private static readonly BurnInOverlay OverFill = new(1280, 720, 30, BurnInBase.Fill);
    private static readonly BurnInOverlay OverBackground = new(1280, 720, 30, BurnInBase.Background, "/backgrounds/loop.mp4");

    [Fact]
    public void BuildArguments_WithoutBurnIn_HasNoPipeAndNoOverlay()
    {
        var arguments = HlsMediaStreamService.BuildArguments("/songs/a.mp4", TimeSpan.Zero, 0, 0, 2);

        Assert.DoesNotContain("pipe:0", arguments);
        Assert.DoesNotContain("overlay", arguments);
    }

    [Fact]
    public void BuildArguments_BurningIn_ReadsThePaintedFramesOffThePipe()
    {
        var arguments = HlsMediaStreamService.BuildArguments(
            "/songs/a.mp4", TimeSpan.Zero, 0, 0, 2, burnIn: OverSource);

        Assert.Contains(" -f rawvideo -pix_fmt rgba -s 1280x720 -r 30 -thread_queue_size 64 -i pipe:0", arguments);
    }

    /// <summary>The picture is the base and the words the overlay: the other way round composites
    /// onto transparency and the words vanish from an encode that still runs.</summary>
    [Fact]
    public void BuildArguments_BurningIn_LaysTheWordsOverTheSourcesOwnPicture()
    {
        var arguments = HlsMediaStreamService.BuildArguments(
            "/songs/a.mp4", TimeSpan.Zero, 0, 0, 2, burnIn: OverSource);

        Assert.Contains("[0:v:0]fps=30,scale=1280:720:force_original_aspect_ratio=decrease,pad=1280:720:(ow-iw)/2:(oh-ih)/2,setsar=1[base]", arguments);
        Assert.Contains("[base][1:v]overlay=0:0:eof_action=pass[v]", arguments);
        Assert.Contains("-map \"[v]\"", arguments);
    }

    /// <summary>A source with no picture has black under the words, which ends with the words since
    /// it never ends by itself.</summary>
    [Fact]
    public void BuildArguments_BurningInOverNothing_PaintsOverBlackThatEndsWithTheWords()
    {
        var arguments = HlsMediaStreamService.BuildArguments(
            "/songs/a.mka", TimeSpan.Zero, 0, 0, 2, burnIn: OverFill);

        Assert.Contains(" -f lavfi -i color=c=black:s=1280x720:r=30", arguments);
        Assert.Contains("[2:v]setsar=1[base];[base][1:v]overlay=0:0:shortest=1[v]", arguments);
    }

    [Fact]
    public void BuildArguments_BurningInOverABackground_LoopsItAndCoversTheFrame()
    {
        var arguments = HlsMediaStreamService.BuildArguments(
            "/songs/a.mka", TimeSpan.Zero, 0, 0, 2, burnIn: OverBackground);

        Assert.Contains(" -stream_loop -1 -i \"/backgrounds/loop.mp4\"", arguments);
        Assert.Contains("[2:v]scale=1280:720:force_original_aspect_ratio=increase,crop=1280:720,setsar=1,fps=30[base]", arguments);
        Assert.Contains("overlay=0:0:shortest=1[v]", arguments);
    }

    /// <summary>Every map is explicit once the graph names the picture, so the sound has to be
    /// named too — and tolerated missing, or a silent source fails the whole encode.</summary>
    [Fact]
    public void BuildArguments_BurningIn_MapsTheSourcesSoundAlongsideThePicture()
    {
        var arguments = HlsMediaStreamService.BuildArguments(
            "/songs/a.mp4", TimeSpan.Zero, 0, 0, 2, burnIn: OverSource);

        Assert.Contains("-map 0:a:0?", arguments);
    }

    [Fact]
    public void BuildArguments_BurningInWithACompanion_TakesTheSoundFromIt()
    {
        var arguments = HlsMediaStreamService.BuildArguments(
            "/songs/a.cdg", TimeSpan.Zero, 0, 0, 2, "/songs/a.mp3", burnIn: OverSource);

        Assert.Contains("[base][2:v]overlay", arguments);
        Assert.Contains("-map 1:a:0?", arguments);

        // A -map before a later -i is an input option ffmpeg refuses.
        Assert.DoesNotContain("-map 0:v:0 -map 1:a:0", arguments);
    }

    /// <summary>An output seek placed ahead of the pipe would bind to the pipe as an input seek.</summary>
    [Fact]
    public void BuildArguments_BurningInGraphics_SeeksOnTheOutputAfterEveryInput()
    {
        var arguments = HlsMediaStreamService.BuildArguments(
            "/songs/a.cdg", TimeSpan.FromSeconds(42), 0, 0, 2, "/songs/a.mp3", burnIn: OverSource);

        Assert.True(
            arguments.IndexOf(" -ss 42.000", StringComparison.Ordinal) > arguments.IndexOf("pipe:0", StringComparison.Ordinal),
            arguments);
    }

    [Fact]
    public void BuildArguments_BurningIn_KeepsTheKeyAndTheTempo()
    {
        var arguments = HlsMediaStreamService.BuildArguments(
            "/songs/a.mp4", TimeSpan.Zero, 2, -10, 2, burnIn: OverSource);

        Assert.Contains("asetrate=44100*", arguments);
        Assert.Contains("atempo=", arguments);

        // The picture is retimed inside the graph, before it meets the words painted in output time.
        Assert.Contains("[0:v:0]setpts=PTS/0.900000,fps=30,", arguments);
        Assert.DoesNotContain(" -vf ", arguments);
    }

    [Fact]
    public void BuildArguments_BurningIn_KeepsTheVoiceLevelsInTheSameGraph()
    {
        var arguments = HlsMediaStreamService.BuildArguments(
            "/songs/a.mp4", TimeSpan.Zero, 0, 0, 2, null,
            new AudioMix(
            [
                new AudioTrack(0, AudioTrackRole.Music, "Instrumental"),
                new AudioTrack(1, AudioTrackRole.Backing, "Backing Vocal"),
                new AudioTrack(2, AudioTrackRole.Lead, "Lead Vocal"),
            ], 40, 80),
            OverSource);

        Assert.Contains("[0:a:2]volume=0.400[l2]", arguments);
        Assert.Contains("overlay=0:0:eof_action=pass[v];[0:a:0]volume=1.000[m0]", arguments);
        Assert.Contains("-map \"[v]\" -map \"[a]\"", arguments);
        Assert.Single(arguments.Split("-filter_complex").Skip(1));
    }

    [Fact]
    public void BuildArguments_BurningIn_KeepsKeyframesOnTheClock()
    {
        var arguments = HlsMediaStreamService.BuildArguments(
            "/songs/a.mp4", TimeSpan.Zero, 0, 0, 2, burnIn: OverSource);

        Assert.Contains("-force_key_frames \"expr:gte(t,n_forced*2)\" -sc_threshold 0", arguments);
    }

    [Fact]
    public void BuildArguments_BurningInGraphics_HoldsThePictureUntilTheAudioEnds()
    {
        var arguments = HlsMediaStreamService.BuildArguments(
            "/songs/a.cdg", TimeSpan.Zero, 0, 0, 2, "/songs/a.mp3", burnIn: OverSource);

        Assert.Contains("[0:v:0]tpad=stop=-1:stop_mode=clone,fps=30,", arguments);
        Assert.Contains(" -shortest ", arguments);
    }

    [Fact]
    public void BuildArguments_BurningIntoAVideo_LeavesItsEndAlone()
    {
        var arguments = HlsMediaStreamService.BuildArguments(
            "/songs/a.mp4", TimeSpan.Zero, 0, 0, 2, burnIn: OverSource);

        Assert.DoesNotContain("tpad", arguments);
        Assert.DoesNotContain("-shortest", arguments);
    }

    [Fact]
    public void BuildArguments_BurningInGraphics_KeepsTheConstantFrameRate()
    {
        var arguments = HlsMediaStreamService.BuildArguments(
            "/songs/a.cdg", TimeSpan.Zero, 0, 0, 2, "/songs/a.mp3", burnIn: OverSource);

        Assert.Contains(" -r 30 ", arguments);
    }
}
