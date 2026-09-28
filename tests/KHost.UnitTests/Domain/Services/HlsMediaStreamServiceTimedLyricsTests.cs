using KHost.Abstractions.Models;
using KHost.Domain.Services;
using KHost.Domain.Services.BurnIn;

namespace KHost.UnitTests.Domain.Services;

/// <summary>The encode for a song whose timed words a display draws itself: the source's own moving
/// picture or none, never cover art, and nothing from an audio file.</summary>
public class HlsMediaStreamServiceTimedLyricsTests
{
    private static readonly AudioMix Mix = new(
    [
        new AudioTrack(0, AudioTrackRole.Music, "Instrumental"),
        new AudioTrack(1, AudioTrackRole.Lead, "Lead Vocal"),
    ], 40, 80);

    [Fact]
    public void BuildArguments_TimedWordsOverAnAudioFile_EncodesSoundAlone()
    {
        var arguments = Timed("/songs/a.mp3");

        Assert.Contains(" -map 0:a:0? ", arguments);
        Assert.DoesNotContain("0:v", arguments);
        Assert.DoesNotContain("0:V", arguments);
        Assert.DoesNotContain("libx264", arguments);
    }

    [Fact]
    public void BuildArguments_TimedWordsOverAnAudioFileAtAnotherKeyAndTempo_EncodesSoundAlone()
    {
        var arguments = Timed("/songs/a.mp3", pitch: 2, tempo: -10);

        Assert.Contains(" -map 0:a:0? -af \"", arguments);
        Assert.DoesNotContain(" -vf ", arguments);
        Assert.DoesNotContain("libx264", arguments);
    }

    [Fact]
    public void BuildArguments_TimedWordsOverAMixedAudioFile_MapsTheMixAlone()
    {
        var arguments = Timed("/songs/a.m4a", mix: Mix);

        Assert.Contains("\" -map \"[a]\"", arguments);
        Assert.DoesNotContain("0:v", arguments);
        Assert.DoesNotContain("0:V", arguments);
        Assert.DoesNotContain("libx264", arguments);
    }

    /// <summary>Capital V: a video keeps its picture while a cover stored beside it is left out.</summary>
    [Fact]
    public void BuildArguments_TimedWordsOverAVideo_KeepsOnlyItsMovingPicture()
    {
        var arguments = Timed("/songs/a.mp4");

        Assert.Contains(" -map 0:V:0? -map 0:a:0? ", arguments);
        Assert.Contains("libx264", arguments);
    }

    [Fact]
    public void BuildArguments_TimedWordsOverAMixedVideo_KeepsOnlyItsMovingPicture()
    {
        var arguments = Timed("/songs/a.mp4", mix: Mix);

        Assert.Contains(" -map 0:V:0? -map \"[a]\"", arguments);
        Assert.DoesNotContain("0:v:0?", arguments);
    }

    /// <summary>With no timed words an MP3 is left to ffmpeg's own pick, which encodes its cover art
    /// as a one-frame picture, as it always has.</summary>
    [Fact]
    public void BuildArguments_NoTimedWords_LeavesAnAudioFilesPictureAlone()
    {
        var arguments = HlsMediaStreamService.BuildArguments("/songs/a.mp3", TimeSpan.Zero, 0, 0, 2);

        Assert.DoesNotContain("-map", arguments);
        Assert.Contains("libx264", arguments);
    }

    [Fact]
    public void BuildArguments_NoTimedWords_KeepsAMixedVideosPictureMapAsItWas()
        => Assert.Contains(
            " -map 0:v:0? -map \"[a]\"",
            HlsMediaStreamService.BuildArguments("/songs/a.mp4", TimeSpan.Zero, 0, 0, 2, mix: Mix));

    /// <summary>A burn-in and a .cdg pair already name every stream they carry.</summary>
    [Fact]
    public void BuildArguments_TimedWordsBurnedInOrOnAGraphicsPair_AreNotRemapped()
    {
        var fill = new BurnInOverlay(1280, 720, 30, BurnInBase.Fill);

        Assert.Equal(
            HlsMediaStreamService.BuildArguments("/songs/a.mp3", TimeSpan.Zero, 0, 0, 2, burnIn: fill),
            HlsMediaStreamService.BuildArguments("/songs/a.mp3", TimeSpan.Zero, 0, 0, 2, burnIn: fill, hasTimedLyrics: true));
        Assert.Equal(
            HlsMediaStreamService.BuildArguments("/songs/a.cdg", TimeSpan.Zero, 0, 0, 2, "/songs/a.mp3"),
            HlsMediaStreamService.BuildArguments("/songs/a.cdg", TimeSpan.Zero, 0, 0, 2, "/songs/a.mp3", hasTimedLyrics: true));
    }

    private static string Timed(string source, int pitch = 0, int tempo = 0, AudioMix? mix = null)
        => HlsMediaStreamService.BuildArguments(source, TimeSpan.Zero, pitch, tempo, 2, mix: mix, hasTimedLyrics: true);
}
