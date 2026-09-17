using KHost.Abstractions.Models;
using KHost.Common.Media;

namespace KHost.UnitTests.Common.Media;

/// <summary>Mistyping a still or ad as karaoke gives it a fallback artist in song search.</summary>
public class MediaFormatsTypeTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"khost-formats-{Guid.NewGuid():N}");

    public MediaFormatsTypeTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private string File(string name)
    {
        var path = Path.Combine(_directory, name);
        System.IO.File.WriteAllText(path, string.Empty);

        return path;
    }

    [Theory]
    [InlineData("poster.jpg")]
    [InlineData("card.PNG")]
    [InlineData("loop.webp")]
    public void AStill_IsAnImage(string name)
        => Assert.Equal(MediaType.Image, MediaFormats.TypeForFile(File(name)));

    /// <summary>A record for between singers, which is not a song anyone is going to sing.</summary>
    [Theory]
    [InlineData("free-fallin.mp3")]
    [InlineData("track.FLAC")]
    [InlineData("bed.m4a")]
    public void ARecord_IsAudio(string name)
        => Assert.Equal(MediaType.Audio, MediaFormats.TypeForFile(File(name)));

    /// <summary>The graphics half says so outright.</summary>
    [Fact]
    public void ACdg_IsKaraoke()
        => Assert.Equal(MediaType.Karaoke, MediaFormats.TypeForFile(File("song.cdg")));

    /// <summary>An .mp3 with a .cdg beside it is the audio half of a karaoke pair, not break music.</summary>
    [Fact]
    public void AnMp3WithGraphicsBesideIt_IsKaraokeRatherThanARecord()
    {
        File("paired.cdg");

        Assert.Equal(MediaType.Karaoke, MediaFormats.TypeForFile(File("paired.mp3")));
    }

    /// <summary>A library is mostly karaoke, so that is what an unqualified folder of video is.</summary>
    [Theory]
    [InlineData("song.mp4")]
    [InlineData("clip.MKV")]
    [InlineData("old.avi")]
    public void Video_IsKaraokeUnlessTheHostSaysOtherwise(string name)
        => Assert.Equal(MediaType.Karaoke, MediaFormats.TypeForFile(File(name)));

    /// <summary>A karaoke video and an ad clip share formats, so the host names which folder it is.</summary>
    [Fact]
    public void Video_TheHostSaidTheseAreAds_IsPlainVideo()
        => Assert.Equal(MediaType.Video,
            MediaFormats.TypeForFile(File("spot.mp4"), videoIsKaraoke: false));

    /// <summary>The host's answer is about video, and must not reach anything that is not.</summary>
    [Theory]
    [InlineData("poster.jpg", MediaType.Image)]
    [InlineData("bed.mp3", MediaType.Audio)]
    public void TheHostsAnswerAboutVideo_DoesNotChangeWhatElseIs(string name, MediaType expected)
        => Assert.Equal(expected, MediaFormats.TypeForFile(File(name), videoIsKaraoke: false));

    /// <summary>Every extension the scanner offers has to resolve to something it can import.</summary>
    [Fact]
    public void EveryListedExtension_HasAType()
    {
        var listed = MediaFormats.AudioExtensions
            .Concat(MediaFormats.VideoExtensions)
            .Concat(MediaFormats.ImageExtensions);

        foreach (var extension in listed)
            Assert.Contains(MediaFormats.TypeForFile(File($"sample{extension}")),
                new[] { MediaType.Karaoke, MediaType.Audio, MediaType.Video, MediaType.Image });
    }

    /// <summary>Leading-dot and lowercase, so a caller may compare against them without folding.</summary>
    [Fact]
    public void TheListedExtensions_AreNormalised()
    {
        var listed = MediaFormats.AudioExtensions
            .Concat(MediaFormats.VideoExtensions)
            .Concat(MediaFormats.ImageExtensions)
            .ToList();

        Assert.All(listed, extension => Assert.StartsWith(".", extension));
        Assert.All(listed, extension => Assert.Equal(extension.ToLowerInvariant(), extension));
        Assert.Equal(listed.Count, listed.Distinct().Count());
    }
}
