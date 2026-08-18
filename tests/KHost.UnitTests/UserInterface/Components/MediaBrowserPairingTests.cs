using KHost.UserInterface.Components;

namespace KHost.UnitTests.UserInterface.Components;

public class MediaBrowserPairingTests
{
    [Fact]
    public void GroupKaraokePairs_PairsACdgWithItsMp3()
    {
        var grouped = MediaBrowser.GroupKaraokePairs([File("song.cdg"), File("song.mp3")]);

        var entry = Assert.Single(grouped);
        Assert.Equal("song (CDG + MP3)", entry.Name);
        Assert.Equal(["/media/song.cdg", "/media/song.mp3"], entry.PairedPaths);
    }

    [Fact]
    public void GroupKaraokePairs_LeavesANonMp3NeighbourOnItsOwnRow()
    {
        var grouped = MediaBrowser.GroupKaraokePairs([File("song.cdg"), File("song.flac")]);

        // CD+G pairs with .mp3 and nothing else, so this is a graphics file and a separate track.
        Assert.Equal(2, grouped.Count);
        Assert.All(grouped, e => Assert.Null(e.PairedPaths));
    }

    [Fact]
    public void GroupKaraokePairs_DoesNotSwallowTheOtherFilesSharingTheName()
    {
        var grouped = MediaBrowser.GroupKaraokePairs(
            [File("song.cdg"), File("song.mp3"), File("song.flac"), File("song.jpg")]);

        // Only the two that form the pair are consumed by it; the rest stay browsable.
        Assert.Equal(3, grouped.Count);
        Assert.Contains(grouped, e => e.Name == "song (CDG + MP3)");
        Assert.Contains(grouped, e => e.Extension == "flac");
        Assert.Contains(grouped, e => e.Extension == "jpg");
    }

    [Fact]
    public void GroupKaraokePairs_LeavesALoneCdgAlone()
    {
        var grouped = MediaBrowser.GroupKaraokePairs([File("song.cdg")]);

        Assert.Null(Assert.Single(grouped).PairedPaths);
    }

    [Fact]
    public void GroupKaraokePairs_MatchesTheExtensionRegardlessOfCase()
    {
        var grouped = MediaBrowser.GroupKaraokePairs([File("SONG.CDG", "CDG"), File("song.mp3")]);

        Assert.Single(grouped);
        Assert.NotNull(grouped[0].PairedPaths);
    }

    private static MediaBrowser.FileEntry File(string fileName, string? extension = null)
        => new(
            FullPath: $"/media/{fileName}",
            Name: fileName,
            IsDirectory: false,
            Extension: extension ?? Path.GetExtension(fileName).TrimStart('.'),
            AlreadyImported: false,
            Size: 1024,
            ModifiedDate: new DateTime(2026, 1, 1),
            SupportedFileCount: null,
            PairedPaths: null,
            ParsedTitle: null,
            ParsedArtist: null);
}
