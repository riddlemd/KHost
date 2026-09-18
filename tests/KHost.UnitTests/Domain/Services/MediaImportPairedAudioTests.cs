using KHost.Domain.Services;

namespace KHost.UnitTests.Domain.Services;

/// <summary>A karaoke pair is one song and must become one row. The .cdg is the half that proves
/// the pair is karaoke, so it is the half that is kept.</summary>
public class MediaImportPairedAudioTests : IDisposable
{
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("khost-pair-tests-");

    public void Dispose() => _folder.Delete(recursive: true);

    /// <summary>The bug this exists for: imported as well, the .mp3 becomes a second row for the
    /// same song that plays the backing track against a blank screen.</summary>
    [Fact]
    public void TheAudioHalfOfAPair_IsNotImportedAsItsOwnRow()
    {
        var graphics = Write("song.cdg");
        var audio = Write("song.mp3");

        var kept = MediaImportService.WithoutPairedAudio([graphics, audio]).ToList();

        Assert.Equal([graphics], kept);
    }

    /// <summary>An .mp3 on its own proves nothing about being karaoke, and is a library row like
    /// any other: break music, an ad bed, a singer's own backing track.</summary>
    [Fact]
    public void AnAudioFileWithNoGraphicsBesideIt_IsStillImported()
    {
        var audio = Write("just-a-song.mp3");

        Assert.Equal([audio], MediaImportService.WithoutPairedAudio([audio]).ToList());
    }

    /// <summary>The pair is found on disk, not in the list handed over: a host may pick the .mp3
    /// alone out of a folder where the .cdg sits beside it.</summary>
    [Fact]
    public void TheAudioHalf_IsDroppedEvenWhenTheGraphicsWereNotSelected()
    {
        Write("song.cdg");
        var audio = Write("song.mp3");

        Assert.Empty(MediaImportService.WithoutPairedAudio([audio]));
    }

    [Fact]
    public void TheGraphicsHalf_IsAlwaysKept()
    {
        var graphics = Write("song.cdg");
        Write("song.mp3");

        Assert.Equal([graphics], MediaImportService.WithoutPairedAudio([graphics]).ToList());
    }

    /// <summary>Extensions arrive however the filesystem spells them.</summary>
    [Fact]
    public void ThePairing_IgnoresTheCaseOfTheExtension()
    {
        Write("song.cdg");
        var audio = Write("song.MP3");

        Assert.Empty(MediaImportService.WithoutPairedAudio([audio]));
    }

    /// <summary>Video is never the audio half of anything, and a .cdg beside one is coincidence.
    /// </summary>
    [Fact]
    public void AVideo_IsNeverDroppedForAGraphicsFileBesideIt()
    {
        Write("song.cdg");
        var video = Write("song.mp4");

        Assert.Equal([video], MediaImportService.WithoutPairedAudio([video]).ToList());
    }

    private string Write(string name)
    {
        var path = Path.Combine(_folder.FullName, name);
        File.WriteAllText(path, "x");
        return path;
    }
}
