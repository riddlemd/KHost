using KHost.Abstractions.Models;
using KHost.Domain.Models;

namespace KHost.UnitTests.Domain.Models;

public class SongTests
{
    [Fact]
    public void Song_InitializesWithNewGuidId()
    {
        var song = new Song { FilePath = "/path/to/song.mp4", Title = "Song" };

        Assert.NotEqual(Guid.Empty, song.Id);
    }

    [Fact]
    public void Song_HasRandomDurationByDefault()
    {
        var song = new Song { FilePath = "/path/to/song.mp4", Title = "Song" };

        Assert.NotNull(song.Duration);
        Assert.True(song.Duration!.Value.TotalSeconds >= 30);
    }

    [Fact]
    public void Song_DefaultStatusIsUnknown()
    {
        var song = new Song { FilePath = "/path/to/song.mp4", Title = "Song" };

        Assert.Equal(SongStatus.Unknown, song.Status);
    }

    [Fact]
    public void Song_DateAddedAndModifiedAreRecent()
    {
        var before = DateTime.UtcNow.AddSeconds(-5);
        var song = new Song { FilePath = "/path/to/song.mp4", Title = "Song" };
        var after = DateTime.UtcNow.AddSeconds(5);

        Assert.InRange(song.DateAdded.UtcDateTime, before, after);
        Assert.InRange(song.DateModified.UtcDateTime, before, after);
    }

    [Fact]
    public void Song_DefaultStringFieldsAreInitialized()
    {
        var song = new Song { FilePath = "/path/to/song.mp4", Title = "Song" };

        Assert.Equal(string.Empty, song.Artist);
        Assert.Equal(string.Empty, song.Album);
        Assert.Equal(string.Empty, song.Format);
        Assert.Equal("", song.Notes);
    }

    [Fact]
    public void Song_ImplementsISong()
    {
        var song = new Song { FilePath = "/path/to/song.mp4", Title = "Song" };

        Assert.IsAssignableFrom<ISong>(song);
    }

    [Fact]
    public void Song_TwoInstancesHaveDistinctIds()
    {
        var a = new Song { FilePath = "/path/a.mp4", Title = "A" };
        var b = new Song { FilePath = "/path/b.mp4", Title = "B" };

        Assert.NotEqual(a.Id, b.Id);
    }
}
